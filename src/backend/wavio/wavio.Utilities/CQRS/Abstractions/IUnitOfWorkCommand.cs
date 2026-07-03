namespace Wavio.Utilities.CQRS.Abstractions;

/// <summary>
/// Marker applied to a command that must execute inside a single database transaction.
/// The <c>TransactionBehavior</c> opens, commits, or rolls back a unit of work for any
/// request implementing this interface; commands without it run without an ambient transaction.
/// </summary>
public interface IUnitOfWorkCommand
{
}
