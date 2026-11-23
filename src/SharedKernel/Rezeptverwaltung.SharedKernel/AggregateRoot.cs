namespace Rezeptverwaltung.SharedKernel;

/// <summary>
/// Konsistenzgrenze eines Aggregats. Änderungen erfolgen nur über Methoden des Aggregats;
/// fachlich relevante Ereignisse werden gesammelt und nach dem Speichern veröffentlicht.
/// </summary>
public abstract class AggregateRoot<TId> : Entity<TId>
    where TId : struct, IEquatable<TId>
{
    private readonly List<IDomainEvent> _domainEvents = [];

    protected AggregateRoot(TId id) : base(id)
    {
    }

    public IReadOnlyCollection<IDomainEvent> DomainEvents => _domainEvents.AsReadOnly();

    public void DomainEventsLeeren() => _domainEvents.Clear();

    protected void Melde(IDomainEvent domainEvent) => _domainEvents.Add(domainEvent);
}
