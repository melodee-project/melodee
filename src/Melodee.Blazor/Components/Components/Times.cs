namespace Melodee.Blazor.Components.Components;

public record Times
{
    private Times()
    {
    }

    /// <summary>
    ///     Occurrence count
    /// </summary>
    public ulong Count { get; init; }

    /// <summary>
    ///     Should occur only once
    /// </summary>
    /// <returns></returns>
    public static Times Once()
    {
        return new Times { Count = 1 };
    }

    /// <summary>
    ///     Should occur until stopped
    /// </summary>
    /// <returns></returns>
    public static Times Infinite()
    {
        return new Times { Count = ulong.MaxValue };
    }

    /// <summary>
    ///     Should occur exactly to the given number of times
    /// </summary>
    /// <param name="count">N occurrence</param>
    /// <returns></returns>
    public static Times Exactly(ulong count)
    {
        return new Times { Count = count };
    }
}
