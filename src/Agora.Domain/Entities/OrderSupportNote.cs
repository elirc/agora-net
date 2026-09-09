using Agora.Domain.Common;

namespace Agora.Domain.Entities;

public class OrderSupportNote
{
    public Guid Id { get; private set; } = Guid.NewGuid();
    public Guid OrderId { get; private set; }
    public Guid AuthorAdminId { get; private set; }
    public string Body { get; private set; } = "";
    public DateTimeOffset CreatedAt { get; private set; }
    private OrderSupportNote() { }
    public OrderSupportNote(Guid orderId, Guid actor, string body, DateTimeOffset now)
    {
        var text = body.Trim();
        if (text.Length is < 1 or > 1000) throw new DomainException("A support note requires 1 to 1,000 characters after trimming.");
        OrderId = orderId; AuthorAdminId = actor; Body = text; CreatedAt = now;
    }
}
