// Copyright (c) Microsoft. All rights reserved.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
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
            MethodCallExpression method => this.TranslateMethodCall(method),
            QueryParameterExpression { Value: var boolValue } parameter when parameter.Type == typeof(bool)
                => boolValue is true ? "true" : "false",
            UnaryExpression { NodeType: ExpressionType.Not } not => $"NOT ({this.TranslateNode(not.Operand)})",
            UnaryExpression { NodeType: ExpressionType.Convert } convert => this.TranslateNode(convert.Operand),
            Expression expr when expr.Type == typeof(bool) && this.TryBindProperty(expr, out var property)
                => this.GenerateComparison(property, true, ExpressionType.Equal),
            _ => throw new NotSupportedException($"Unsupported expression node '{node?.NodeType}'.")
        };

    private string TranslateComparison(BinaryExpression binary)
    {
        if (this.TryBindProperty(binary.Left, out var property) && this.TryExtractValue(binary.Right, out var constant))
        {
            return this.GenerateComparison(property, constant, binary.NodeType);
        }

        if (this.TryBindProperty(binary.Right, out property) && this.TryExtractValue(binary.Left, out var leftConstant))
        {
            return this.GenerateComparison(property, leftConstant, binary.NodeType);
        }

        throw new NotSupportedException("LiteDB filter translation expects member-to-constant comparisons.");
    }

    private string TranslateMethodCall(MethodCallExpression method)
    {
        if (this.TryBindProperty(method, out var property))
        {
            return this.GenerateComparison(property, true, ExpressionType.Equal);
        }

        if (method.Method.DeclaringType == typeof(string) && method.Object is { } instance && this.TryBindProperty(instance, out property))
        {
            return method.Method.Name switch
            {
                nameof(string.Contains) => this.TranslateStringPattern(property, method.Arguments, PatternKind.Contains, method.Method.Name),
                nameof(string.StartsWith) => this.TranslateStringPattern(property, method.Arguments, PatternKind.StartsWith, method.Method.Name),
                nameof(string.EndsWith) => this.TranslateStringPattern(property, method.Arguments, PatternKind.EndsWith, method.Method.Name),
                _ => throw new NotSupportedException($"String method '{method.Method.Name}' is not supported in LiteDB filters.")
            };
        }

        if (IsEnumerableContains(method, out var source, out var item))
        {
            return this.TranslateContains(source, item);
        }

        throw new NotSupportedException($"Unsupported method call '{method.Method.DeclaringType?.Name}.{method.Method.Name}'.");
    }

    private string TranslateContains(Expression source, Expression item)
    {
        if (!this.TryBindProperty(item, out var property))
        {
            if (this.TryBindProperty(source, out property) && this.TryExtractValue(item, out var element))
            {
                var placeholder = this.AddParameter(CreateParameter(element));
                return $"({placeholder} IN $.{property.StorageName})";
            }

            throw new NotSupportedException("LiteDB filters support set membership between collection properties and constant values only.");
        }

        var values = this.MaterializeValues(source);
        var placeholderArray = this.AddParameter(values);
        return $"($.{property.StorageName} IN {placeholderArray})";
    }

    private string TranslateStringPattern(PropertyModel property, IReadOnlyList<Expression> arguments, PatternKind kind, string methodName)
    {
        if (arguments.Count is < 1 or > 2)
        {
            throw new NotSupportedException($"String method '{methodName}' must specify a single comparison value.");
        }

        if (!this.TryExtractValue(arguments[0], out var argumentValue))
        {
            throw new NotSupportedException($"String method '{methodName}' requires a constant comparison value.");
        }

        if (argumentValue is not string text)
        {
            throw new InvalidCastException($"String method '{methodName}' requires a string constant, but '{argumentValue?.GetType().Name ?? "null"}' was provided.");
        }

        if (arguments.Count == 2)
        {
            if (!this.TryExtractValue(arguments[1], out var comparisonValue) || comparisonValue is not StringComparison stringComparison)
            {
                throw new NotSupportedException($"String method '{methodName}' only supports constant {nameof(StringComparison)} values.");
            }

            if (stringComparison != StringComparison.Ordinal)
            {
                throw new NotSupportedException($"String method '{methodName}' only supports {nameof(StringComparison.Ordinal)}.");
            }
        }

        var pattern = kind switch
        {
            PatternKind.Contains => $"%{EscapeLike(text)}%",
            PatternKind.StartsWith => $"{EscapeLike(text)}%",
            PatternKind.EndsWith => $"%{EscapeLike(text)}",
            _ => throw new NotSupportedException($"Pattern '{kind}' is not supported.")
        };

        var placeholder = this.AddParameter(new BsonValue(pattern));
        return $"($.{property.StorageName} LIKE {placeholder})";
    }

    private BsonArray MaterializeValues(Expression source)
    {
        if (!this.TryExtractValue(source, out var value))
        {
            throw new NotSupportedException("Set membership clauses must compare against a constant or captured collection.");
        }

        if (value is string)
        {
            throw new NotSupportedException("Set membership cannot compare against a string. Provide a collection of values instead.");
        }

        if (value is not System.Collections.IEnumerable enumerable)
        {
            throw new NotSupportedException("Set membership clauses require a collection value.");
        }

        var array = new BsonArray();
        foreach (var element in enumerable)
        {
            array.Add(element switch
            {
                BsonValue bsonElement => bsonElement,
                _ => new BsonValue(element)
            });
        }

        return array;
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
                ExpressionType.Equal => $"({field} = null)",
                ExpressionType.NotEqual => $"({field} != null)",
                _ => throw new NotSupportedException("Null comparisons are only supported for equality checks.")
            };
        }

        var placeholder = this.AddParameter(CreateParameter(value));
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

    private static BsonValue CreateParameter(object? value)
    {
        if (value is BsonValue bson)
        {
            return bson;
        }

        if (value is System.Collections.IEnumerable enumerable && value is not string)
        {
            var array = new BsonArray();
            foreach (var element in enumerable)
            {
                array.Add(element switch
                {
                    BsonValue bsonElement => bsonElement,
                    _ => new BsonValue(element)
                });
            }

            return array;
        }

        return new BsonValue(value);
    }

    private bool TryExtractValue(Expression expression, out object? value)
    {
        switch (expression)
        {
            case ConstantExpression { Value: var constant }:
                value = constant;
                return true;
            case QueryParameterExpression { Value: var parameter }:
                value = parameter;
                return true;
            case NewArrayExpression { NodeType: ExpressionType.NewArrayInit } newArray:
                var elementType = newArray.Type.GetElementType() ?? typeof(object);
                var array = Array.CreateInstance(elementType, newArray.Expressions.Count);
                for (var i = 0; i < newArray.Expressions.Count; i++)
                {
                    if (!this.TryExtractValue(newArray.Expressions[i], out var elementValue))
                    {
                        throw new NotSupportedException("Set membership clauses must use constant values.");
                    }

                    array.SetValue(elementValue, i);
                }

                value = array;
                return true;
            default:
                value = null;
                return false;
        }
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

    private static bool IsEnumerableContains(MethodCallExpression methodCall, out Expression source, out Expression item)
    {
        if (methodCall.Method.Name != nameof(Enumerable.Contains))
        {
            source = null!;
            item = null!;
            return false;
        }

        if (methodCall.Method.DeclaringType == typeof(Enumerable) && methodCall.Arguments.Count == 2)
        {
            source = methodCall.Arguments[0];
            item = methodCall.Arguments[1];
            return true;
        }

        if (methodCall.Object is not null && methodCall.Arguments.Count == 1)
        {
            source = methodCall.Object;
            item = methodCall.Arguments[0];
            return true;
        }

        source = null!;
        item = null!;
        return false;
    }

    private static string EscapeLike(string text)
        => text
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("%", "\\%", StringComparison.Ordinal)
            .Replace("_", "\\_", StringComparison.Ordinal);

    private enum PatternKind
    {
        Contains,
        StartsWith,
        EndsWith
    }
}
