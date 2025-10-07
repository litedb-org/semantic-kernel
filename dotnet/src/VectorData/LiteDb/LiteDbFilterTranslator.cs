// Copyright (c) Microsoft. All rights reserved.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
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
            MethodCallExpression method => this.TranslateMethodCall(method),
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
        if (this.TryBindProperty(binary.Left, out var property) && this.TryExtractConstant(binary.Right, out var constant))
        {
            return this.GenerateComparison(property, constant, binary.NodeType);
        }

        if (this.TryBindProperty(binary.Right, out property) && this.TryExtractConstant(binary.Left, out var leftConstant))
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

        var placeholder = this.AddParameter(this.CreateParameterValue(value));
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

    private string TranslateMethodCall(MethodCallExpression methodCall)
    {
        if (methodCall.Method.DeclaringType == typeof(string)
            && methodCall.Method.Name is nameof(string.Contains) or nameof(string.StartsWith) or nameof(string.EndsWith)
            && methodCall.Object is { } stringObject
            && this.TryBindProperty(stringObject, out var stringProperty))
        {
            return this.TranslateStringMethod(methodCall, stringProperty);
        }

        if (methodCall is { Method.Name: nameof(Enumerable.Contains), Method.DeclaringType: var declaringType } enumerableCall
            && declaringType == typeof(Enumerable))
        {
            return this.TranslateEnumerableContains(enumerableCall);
        }

        if (methodCall.Method.Name == nameof(List<int>.Contains)
            && methodCall.Object is not null
            && this.TryBindProperty(methodCall.Object, out var collectionProperty))
        {
            return this.TranslateCollectionContains(methodCall, collectionProperty);
        }

        if (methodCall.Method.Name == "Contains"
            && methodCall.Object is not null
            && methodCall.Method.DeclaringType is { } collectionDeclaringType
            && collectionDeclaringType != typeof(string)
            && this.TryBindProperty(methodCall.Object, out collectionProperty))
        {
            return this.TranslateCollectionContains(methodCall, collectionProperty);
        }

        throw new NotSupportedException($"Unsupported method call '{methodCall.Method.DeclaringType?.Name}.{methodCall.Method.Name}' in LiteDB filter expression.");
    }

    private string TranslateStringMethod(MethodCallExpression methodCall, PropertyModel property)
    {
        var propertyType = Nullable.GetUnderlyingType(property.Type) ?? property.Type;
        if (propertyType != typeof(string))
        {
            throw new NotSupportedException($"String method '{methodCall.Method.Name}' is only supported on string properties, but '{property.ModelName}' is of type '{property.Type.Name}'.");
        }

        if (methodCall.Arguments.Count == 0)
        {
            throw new NotSupportedException($"Method '{methodCall.Method.Name}' on property '{property.ModelName}' must specify a value argument.");
        }

        if (methodCall.Arguments.Count > 1)
        {
            throw new NotSupportedException($"LiteDB filters only support the overload of '{methodCall.Method.Name}' with a single value argument.");
        }

        if (!this.TryExtractConstant(methodCall.Arguments[0], out var value) || value is not string stringValue)
        {
            throw new NotSupportedException($"LiteDB filters require '{methodCall.Method.Name}' arguments to be constant strings.");
        }

        var pattern = methodCall.Method.Name switch
        {
            nameof(string.Contains) => $"%{EscapeLikePattern(stringValue)}%",
            nameof(string.StartsWith) => $"{EscapeLikePattern(stringValue)}%",
            nameof(string.EndsWith) => $"%{EscapeLikePattern(stringValue)}",
            _ => throw new NotSupportedException($"Unsupported string method '{methodCall.Method.Name}'.")
        };

        var placeholder = this.AddParameter(new BsonValue(pattern));
        var field = GetField(property);
        return $"({field} LIKE {placeholder})";
    }

    private string TranslateCollectionContains(MethodCallExpression methodCall, PropertyModel property)
    {
        if (!typeof(IEnumerable).IsAssignableFrom((Nullable.GetUnderlyingType(property.Type) ?? property.Type)))
        {
            throw new NotSupportedException($"Collection.Contains is only supported for enumerable properties. Property '{property.ModelName}' has type '{property.Type.Name}'.");
        }

        if (methodCall.Arguments.Count != 1)
        {
            throw new NotSupportedException($"Method '{methodCall.Method.Name}' must have exactly one argument in LiteDB filters.");
        }

        if (!this.TryExtractConstant(methodCall.Arguments[0], out var value))
        {
            throw new NotSupportedException("LiteDB filters require collection.Contains arguments to be constant values.");
        }

        var placeholder = this.AddParameter(this.CreateParameterValue(value));
        var field = GetField(property);
        return $"({field} ANY = {placeholder})";
    }

    private string TranslateEnumerableContains(MethodCallExpression methodCall)
    {
        if (methodCall.Arguments.Count != 2)
        {
            throw new NotSupportedException("Enumerable.Contains must specify the source and the item to compare.");
        }

        var source = methodCall.Arguments[0];
        var item = methodCall.Arguments[1];

        if (this.TryBindProperty(source, out var collectionProperty))
        {
            var propertyType = Nullable.GetUnderlyingType(collectionProperty.Type) ?? collectionProperty.Type;
            if (!typeof(IEnumerable).IsAssignableFrom(propertyType))
            {
                throw new NotSupportedException($"Enumerable.Contains is only supported on enumerable properties. Property '{collectionProperty.ModelName}' has type '{collectionProperty.Type.Name}'.");
            }

            if (!this.TryExtractConstant(item, out var element))
            {
                throw new NotSupportedException("LiteDB filters require Enumerable.Contains item arguments to be constant values when the source is a record property.");
            }

            var collectionPlaceholder = this.AddParameter(this.CreateParameterValue(element));
            var collectionField = GetField(collectionProperty);
            return $"({collectionField} ANY = {collectionPlaceholder})";
        }

        if (!this.TryExtractConstant(source, out var values))
        {
            try
            {
                values = Expression.Lambda(source).Compile().DynamicInvoke();
            }
            catch
            {
                throw new NotSupportedException("LiteDB filters require Enumerable.Contains sources to be constant sequences.");
            }
        }

        if (values is not IEnumerable enumerable)
        {
            throw new NotSupportedException("LiteDB filters require Enumerable.Contains sources to be constant sequences.");
        }

        if (!this.TryBindProperty(item, out var property))
        {
            throw new NotSupportedException("LiteDB filters support Enumerable.Contains only when comparing against a record property.");
        }

        var placeholder = this.AddParameter(this.CreateParameterValue(enumerable));
        var field = GetField(property);
        return $"({field} IN {placeholder})";
    }

    private string AddParameter(BsonValue value)
    {
        var index = this._parameters.Count;
        this._parameters.Add(value);
        return $"@{index}";
    }

    private bool TryExtractConstant(Expression expression, out object? value)
    {
        switch (expression)
        {
            case ConstantExpression constant:
                value = constant.Value;
                return true;
            case QueryParameterExpression queryParameter:
                value = queryParameter.Value;
                return true;
            case MemberExpression { Expression: QueryParameterExpression queryParameter, Member: PropertyInfo property }
                when property.CanRead:
                value = property.GetValue(queryParameter);
                return true;
            case MemberExpression { Expression: { } instanceExpression } member
                when this.TryExtractConstant(instanceExpression, out var instance)
                && TryGetMemberValue(member.Member, instance, out value):
                return true;
            case MemberExpression { Expression: null } member when TryGetMemberValue(member.Member, null, out value):
                return true;
            case UnaryExpression { NodeType: ExpressionType.Convert or ExpressionType.ConvertChecked } unary
                when this.TryExtractConstant(unary.Operand, out var operand):
                value = operand;
                return true;
            case NewArrayExpression newArray:
            {
                var items = new object?[newArray.Expressions.Count];
                for (var i = 0; i < newArray.Expressions.Count; i++)
                {
                    if (!this.TryExtractConstant(newArray.Expressions[i], out var element))
                    {
                        value = null;
                        return false;
                    }

                    items[i] = element;
                }

                value = items;
                return true;
            }
            default:
                value = null;
                return false;
        }
    }

    private BsonValue CreateParameterValue(object? value)
    {
        switch (value)
        {
            case null:
                return BsonValue.Null;
            case BsonValue bson:
                return bson;
            case IEnumerable enumerable when value is not string:
            {
                var array = new BsonArray();
                foreach (var element in enumerable)
                {
                    array.Add(this.CreateParameterValue(element));
                }

                return array;
            }
            default:
                return new BsonValue(value);
        }
    }

    private static string GetField(PropertyModel property)
        => $"$.{property.StorageName}";

    private static string EscapeLikePattern(string value)
        => value.Replace("%", "[%]").Replace("_", "[_]");

    private static bool IsComparison(ExpressionType type)
        => type is ExpressionType.Equal or ExpressionType.NotEqual
            or ExpressionType.GreaterThan or ExpressionType.GreaterThanOrEqual
            or ExpressionType.LessThan or ExpressionType.LessThanOrEqual;

    private static bool TryGetMemberValue(MemberInfo member, object? instance, out object? value)
    {
        switch (member)
        {
            case FieldInfo field:
                value = field.GetValue(instance);
                return true;
            case PropertyInfo { CanRead: true } property:
                value = property.GetValue(instance);
                return true;
            default:
                value = null;
                return false;
        }
    }

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
