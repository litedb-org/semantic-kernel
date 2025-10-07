// Copyright (c) Microsoft. All rights reserved.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using LiteDB;
using Microsoft.Extensions.VectorData.ProviderServices;
using Microsoft.Extensions.VectorData.ProviderServices.Filter;

namespace Microsoft.SemanticKernel.Connectors.LiteDB;

internal sealed class LiteDbFilterTranslator
{
    private readonly BsonMapper _mapper;
    private CollectionModel _model = null!;
    private ParameterExpression _parameter = null!;

    public LiteDbFilterTranslator(BsonMapper? mapper = null)
    {
        this._mapper = mapper ?? BsonMapper.Global;
    }

    public BsonExpression? Translate(LambdaExpression filter, CollectionModel model)
    {
        if (filter is null)
        {
            return null;
        }

        this._model = model;
        this._parameter = filter.Parameters.Single();

        var preprocessor = new FilterTranslationPreprocessor { SupportsParameterization = false };
        var processed = preprocessor.Preprocess(filter.Body);

        return this.TranslateInternal(processed);
    }

    private BsonExpression? TranslateInternal(Expression? node)
        => node switch
        {
            null => null,
            ConstantExpression { Value: bool value } => value ? null : BsonExpression.Create("false"),
            BinaryExpression binary when IsComparison(binary.NodeType)
                => this.TranslateComparison(binary),
            BinaryExpression binary when binary.NodeType is ExpressionType.AndAlso or ExpressionType.OrElse
                => this.TranslateLogical(binary),
            UnaryExpression { NodeType: ExpressionType.Not } unary
                => this.TranslateNot(unary),
            UnaryExpression { NodeType: ExpressionType.Convert } convert
                when Nullable.GetUnderlyingType(convert.Type) == convert.Operand.Type
                => this.TranslateInternal(convert.Operand),
            Expression booleanExpression when booleanExpression.Type == typeof(bool) && this.TryBindProperty(booleanExpression, out var property)
                => this.CreateEqualityExpression(property, true, ExpressionType.Equal),
            MethodCallExpression methodCall => this.TranslateMethodCall(methodCall),
            _ => throw new NotSupportedException($"Unsupported filter expression node type '{node?.NodeType}'.")
        };

    private BsonExpression TranslateComparison(BinaryExpression binary)
    {
        if (this.TryBindProperty(binary.Left, out var property) && this.TryGetConstant(binary.Right, out var constant))
        {
            return this.CreateEqualityExpression(property, constant, binary.NodeType);
        }

        if (this.TryBindProperty(binary.Right, out property) && this.TryGetConstant(binary.Left, out constant))
        {
            return this.CreateEqualityExpression(property, constant, Flip(binary.NodeType));
        }

        throw new NotSupportedException("LiteDB connector only supports comparisons between a property and a constant value.");

        static ExpressionType Flip(ExpressionType nodeType)
            => nodeType switch
            {
                ExpressionType.GreaterThan => ExpressionType.LessThan,
                ExpressionType.GreaterThanOrEqual => ExpressionType.LessThanOrEqual,
                ExpressionType.LessThan => ExpressionType.GreaterThan,
                ExpressionType.LessThanOrEqual => ExpressionType.GreaterThanOrEqual,
                _ => nodeType
            };
    }

    private BsonExpression? TranslateLogical(BinaryExpression binary)
    {
        var left = this.TranslateInternal(binary.Left);
        var right = this.TranslateInternal(binary.Right);

        if (binary.NodeType == ExpressionType.AndAlso)
        {
            if (left is null)
            {
                return right;
            }

            if (right is null)
            {
                return left;
            }

            return Query.And(left, right);
        }

        if (left is null)
        {
            return right;
        }

        if (right is null)
        {
            return left;
        }

        return Query.Or(left, right);
    }

    private BsonExpression TranslateNot(UnaryExpression not)
    {
        switch (not.Operand)
        {
            case BinaryExpression { NodeType: ExpressionType.Equal or ExpressionType.NotEqual } binary:
                var inverted = Expression.MakeBinary(binary.NodeType == ExpressionType.Equal ? ExpressionType.NotEqual : ExpressionType.Equal, binary.Left, binary.Right);
                return this.TranslateComparison(inverted);
            case var operand when operand.Type == typeof(bool) && this.TryBindProperty(operand, out var property):
                return this.CreateEqualityExpression(property, false, ExpressionType.Equal);
        }

        throw new NotSupportedException("LiteDB connector does not support the logical NOT operator for the provided expression.");
    }

    private BsonExpression TranslateMethodCall(MethodCallExpression methodCall)
    {
        if (methodCall.Method.Name == nameof(Enumerable.Contains))
        {
            if (methodCall.Object is not null)
            {
                // Instance .Contains()
                if (!this.TryBindProperty(methodCall.Object, out var property))
                {
                    throw new NotSupportedException("Contains is only supported against scalar collection constants.");
                }

                throw new NotSupportedException("LiteDB connector does not support searching within array properties in filters.");
            }

            if (methodCall.Arguments is [var source, var item])
            {
                return this.TranslateContains(source, item);
            }
        }

        throw new NotSupportedException($"Unsupported method call in filter: {methodCall.Method.Name}.");
    }

    private BsonExpression TranslateContains(Expression source, Expression item)
    {
        if (!this.TryBindProperty(item, out var property))
        {
            throw new NotSupportedException("Contains requires the item argument to be a property reference.");
        }

        IEnumerable? values = source switch
        {
            ConstantExpression { Value: IEnumerable constant } => constant,
            NewArrayExpression arrayExpression => arrayExpression.Expressions.Select(e => this.Evaluate(e)).ToArray(),
            _ => throw new NotSupportedException("LiteDB connector only supports Contains over constant collections.")
        };

        if (values is null)
        {
            throw new NotSupportedException("LiteDB connector requires a non-null collection for Contains.");
        }

        var bsonValues = new BsonArray();
        foreach (var value in values)
        {
            bsonValues.Add(value is null ? BsonValue.Null : this._mapper.Serialize(property.Type, value));
        }

        return Query.In(property.StorageName, bsonValues);
    }

    private bool TryGetConstant(Expression expression, out object? value)
    {
        switch (expression)
        {
            case ConstantExpression constant:
                value = constant.Value;
                return true;
            case MemberExpression member when member.Expression is ConstantExpression:
                value = this.Evaluate(member);
                return true;
            default:
                value = null;
                return false;
        }
    }

    private object? Evaluate(Expression expression)
        => Expression.Lambda(expression).Compile().DynamicInvoke();

    private bool TryBindProperty(Expression expression, out PropertyModel property)
    {
        if (expression is MemberExpression member && member.Expression == this._parameter)
        {
            if (this._model.PropertyMap.TryGetValue(member.Member.Name, out var found))
            {
                property = found;
                return true;
            }
        }

        property = null!;
        return false;
    }

    private BsonExpression CreateEqualityExpression(PropertyModel property, object? value, ExpressionType nodeType)
    {
        if (value is null)
        {
            return nodeType switch
            {
                ExpressionType.Equal => Query.EQ(property.StorageName, BsonValue.Null),
                ExpressionType.NotEqual => Query.Not(property.StorageName, BsonValue.Null),
                _ => throw new NotSupportedException("LiteDB connector does not support range comparisons against null.")
            };
        }

        var bsonValue = this._mapper.Serialize(value.GetType(), value);

        return nodeType switch
        {
            ExpressionType.Equal => Query.EQ(property.StorageName, bsonValue),
            ExpressionType.NotEqual => Query.Not(property.StorageName, bsonValue),
            ExpressionType.GreaterThan => Query.GT(property.StorageName, bsonValue),
            ExpressionType.GreaterThanOrEqual => Query.GTE(property.StorageName, bsonValue),
            ExpressionType.LessThan => Query.LT(property.StorageName, bsonValue),
            ExpressionType.LessThanOrEqual => Query.LTE(property.StorageName, bsonValue),
            _ => throw new NotSupportedException($"Unsupported comparison operator '{nodeType}'.")
        };
    }

    private static bool IsComparison(ExpressionType nodeType)
        => nodeType is ExpressionType.Equal or ExpressionType.NotEqual
            or ExpressionType.GreaterThan or ExpressionType.GreaterThanOrEqual
            or ExpressionType.LessThan or ExpressionType.LessThanOrEqual;
}
