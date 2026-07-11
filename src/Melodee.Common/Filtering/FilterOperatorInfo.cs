using Melodee.Common.Extensions;

namespace Melodee.Common.Filtering;

/// <summary>
///     Filter operator to dynamically filter IQueryable results.
/// </summary>
/// <param name="PropertyName">Name of object type being filtered.</param>
/// <param name="Operator">Operation of Filter.</param>
/// <param name="Value">Value to filter on.</param>
/// <param name="JoinOperator">The Join condition when more than one filter, e.g. 'OR' or 'AND'</param>
/// <param name="ColumnName">Optional column name to use when building SQL</param>
/// <param name="OperatorOverride">Optional operator text to use instead of the mapped filter operator.</param>
public record FilterOperatorInfo(
    string PropertyName,
    FilterOperator Operator,
    object Value,
    string? JoinOperator = "AND",
    string? ColumnName = null,
    string? OperatorOverride = null)
{
    public const string AndJoinOperator = "AND";

    public const string OrJoinOperator = "OR";

    private const string SqlWildCard = "%";

    public string OperatorValue => FilterOperatorToConditionString(Operator);

    public object ValuePattern()
    {
        if (Value.IsNumericType())
        {
            return Value;
        }

        switch (Operator)
        {
            case FilterOperator.Contains:
            case FilterOperator.DoesNotContain:
                return $"{SqlWildCard}{Value}{SqlWildCard}";
            case FilterOperator.StartsWith:
                return $"{SqlWildCard}{Value}";
            case FilterOperator.EndsWith:
                return $"{Value}{SqlWildCard}";
            case FilterOperator.IsNull:
            case FilterOperator.IsEmpty:
            case FilterOperator.IsNotNull:
            case FilterOperator.IsNotEmpty:
                return string.Empty;
        }

        return Value;
    }

    public static bool IsLikeOperator(FilterOperator filterOperator)
    {
        return filterOperator switch
        {
            FilterOperator.Contains or FilterOperator.StartsWith or FilterOperator.EndsWith
                or FilterOperator.DoesNotContain => true,
            _ => false
        };
    }

    public static string FilterOperatorToConditionString(FilterOperator filterOperator)
    {
        return filterOperator switch
        {
            FilterOperator.NotEquals => "!=",
            FilterOperator.LessThan => "<",
            FilterOperator.LessThanOrEquals => "<=",
            FilterOperator.GreaterThan => ">",
            FilterOperator.GreaterThanOrEquals => ">=",
            FilterOperator.Contains => "LIKE",
            FilterOperator.StartsWith => "LIKE",
            FilterOperator.EndsWith => "LIKE",
            FilterOperator.DoesNotContain => "NOT LIKE",
            FilterOperator.IsNull => "IS NULL",
            FilterOperator.IsNotNull => "IS NOT NULL",
            _ => "="
        };
    }
}
