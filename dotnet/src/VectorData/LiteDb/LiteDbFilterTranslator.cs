// Copyright (c) Microsoft. All rights reserved.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq.Expressions;
using LiteDB;
using Microsoft.Extensions.VectorData.ProviderServices;
using Microsoft.Extensions.VectorData.ProviderServices.Filter;

namespace Microsoft.SemanticKernel.Connectors.LiteDb;

internal sealed class LiteDbFilterTranslator
{
    private CollectionModel _model = null!;
    private ParameterExpression _parameter = null!;
    private readonly List<BsonValue> _parameters = new();

    internal (string Expression, BsonValue[] Parameters) Translate(LambdaExpression expression, CollectionModel model)
    {
        this._model = model;
        this._parameter = expression.Parameters[0];
        this._parameters.Clear();

        var preprocessor = new FilterTranslationPreprocessor { SupportsParameterization = true };
        var preprocessed = preprocessor.Preprocess(expression.Body);

        var exprText = this.TranslateNode(preprocessed);
        return (exprText, this._parameters.ToArray());
    }

    private string TranslateNode(Expression? node)
        => node switch
        {
            null => "true",
            ConstantExpression constant when constant.Value is bool boolValue => boolValue ? "true" : "false",
            BinaryExpression binary when IsComparison(binary.NodeType) => this.TranslateComparison(binary),
            BinaryExpression binary when binary.NodeType is ExpressionType.AndAlso or ExpressionType.OrElse
                => this.TranslateLogical(binary),
            UnaryExpression { NodeType: ExpressionType.Not } not => $"NOT ({this.TranslateNode(not.Operand)})",
            UnaryExpression { NodeType: ExpressionType.Convert } convert => this.TranslateNode(convert.Operand),
            Expression expr when expr.Type == typeof(bool) && this.TryBindProperty(expr, out var property)
                => this.GenerateComparison(property, true, ExpressionType.Equal),
            _ => throw new NotSupportedException($"Unsupported expression node '{node?.NodeType}'.")
        };

    private string TranslateComparison(BinaryExpression binary)
    {
        if (this.TryBindProperty(binary.Left, out var property) && binary.Right is ConstantExpression { Value: var constant })
        {
            return this.GenerateComparison(property, constant, binary.NodeType);
        }

        if (this.TryBindProperty(binary.Right, out property) && binary.Left is ConstantExpression { Value: var leftConstant })
        {
            return this.GenerateComparison(property, leftConstant, binary.NodeType);
        }

        throw new NotSupportedException("LiteDB filter translation expects member-to-constant comparisons.");
    }

    private string TranslateLogical(BinaryExpression binary)
    {
        var left = this.TranslateNode(binary.Left);
        var right = this.TranslateNode(binary.Right);
        var op = binary.NodeType == ExpressionType.AndAlso ? "AND" : "OR";
        return $"({left} {op} {right})";
    }

    private string GenerateComparison(PropertyModel property, object? value, ExpressionType nodeType)
    {
        var field = $"$.{property.StorageName}";
        if (value is null)
        {
            return nodeType switch
            {
                ExpressionType.Equal => $"({field} IS NULL)",
                ExpressionType.NotEqual => $"({field} IS NOT NULL)",
                _ => throw new NotSupportedException("Null comparisons are only supported for equality checks.")
            };
        }

        var placeholder = this.AddParameter(new BsonValue(value));
        var op = nodeType switch
        {
            ExpressionType.Equal => "=",
            ExpressionType.NotEqual => "!=",
            ExpressionType.GreaterThan => ">",
            ExpressionType.GreaterThanOrEqual => ">=",
            ExpressionType.LessThan => "<",
            ExpressionType.LessThanOrEqual => "<=",
            _ => throw new NotSupportedException($"Comparison operator '{nodeType}' is not supported.")
        };

        return $"({field} {op} {placeholder})";
    }

    private string AddParameter(BsonValue value)
    {
        var index = this._parameters.Count;
        this._parameters.Add(value);
        return $"@{index}";
    }

    private static bool IsComparison(ExpressionType type)
        => type is ExpressionType.Equal or ExpressionType.NotEqual
            or ExpressionType.GreaterThan or ExpressionType.GreaterThanOrEqual
            or ExpressionType.LessThan or ExpressionType.LessThanOrEqual;

    private bool TryBindProperty(Expression expression, [NotNullWhen(true)] out PropertyModel? property)
    {
        var unwrapped = expression;
        while (unwrapped is UnaryExpression { NodeType: ExpressionType.Convert } convert)
        {
            unwrapped = convert.Operand;
        }

        string? modelName = unwrapped switch
        {
            MemberExpression member when member.Expression == this._parameter => member.Member.Name,
            MethodCallExpression
            {
                Method: { Name: "get_Item", DeclaringType: var declaringType },
                Arguments: [ConstantExpression { Value: string key }]
            } call when call.Object == this._parameter && declaringType == typeof(Dictionary<string, object?>)
                => key,
            _ => null
        };

        if (modelName is null)
        {
            property = null;
            return false;
        }

        if (!this._model.PropertyMap.TryGetValue(modelName, out property))
        {
            throw new InvalidOperationException($"Property name '{modelName}' provided as part of the filter clause is not a valid property name.");
        }

        var expectedType = Nullable.GetUnderlyingType(property.Type) ?? property.Type;
        unwrapped = expression;
        while (unwrapped is UnaryExpression { NodeType: ExpressionType.Convert } convert)
        {
            var convertType = Nullable.GetUnderlyingType(convert.Type) ?? convert.Type;
            if (convertType != expectedType && convertType != typeof(object))
            {
                throw new InvalidCastException($"Property '{property.ModelName}' is being cast to type '{convert.Type.Name}', but its configured type is '{property.Type.Name}'.");
            }

            unwrapped = convert.Operand;
        }

        return true;
    }
}
