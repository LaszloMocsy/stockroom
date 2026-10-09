namespace Stockroom.Data.Stock;

/// <summary>A void was refused; nothing was written.</summary>
public sealed class MovementNotVoidableException(MovementNotVoidableReason reason)
    : Exception(reason switch
    {
        MovementNotVoidableReason.AlreadyVoided => "The movement has already been voided.",
        _ => "A void cannot itself be voided; record a new movement instead.",
    })
{
    public MovementNotVoidableReason Reason { get; } = reason;
}

public enum MovementNotVoidableReason
{
    /// <summary>The movement was voided before; a movement is reversed at most once.</summary>
    AlreadyVoided,

    /// <summary>The movement is itself a void. Reversing it would quietly re-apply the original.</summary>
    IsVoid,
}
