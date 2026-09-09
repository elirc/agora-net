using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;

namespace Agora.Api.Rendering;

public sealed record PackingSlipAddress(string FullName, string Line1, string? Line2,
    string City, string Region, string PostalCode, string Country);
public sealed record PackingSlipLine(string Sku, string ProductName, string VariantName,
    int Ordered, long Fulfilled, long Remaining);
public sealed record PackingSlipModel(string Number, DateTimeOffset CreatedAt,
    PackingSlipAddress Address, IReadOnlyList<PackingSlipLine> Lines);

/// <summary>Renders only operational snapshots. Every variable occupies an encoded text node.</summary>
public static class PackingSlipRenderer
{
    public static string Render(PackingSlipModel model)
    {
        static string E(string? value) => HtmlEncoder.Default.Encode(value ?? "");
        static string N(long value) => value.ToString(CultureInfo.InvariantCulture);
        var html = new StringBuilder("""
            <!doctype html><html lang="en"><head><meta charset="utf-8">
            <title>Packing slip</title><style>
            @page { margin: 16mm; }
            body { font-family: system-ui, sans-serif; color: #111; margin: 24px; font-size: 12pt; overflow-wrap: anywhere; }
            h1 { font-size: 22pt; } address { font-style: normal; margin-bottom: 24px; }
            table { border-collapse: collapse; width: 100%; table-layout: fixed; }
            th, td { border-bottom: 1px solid #aaa; padding: 8px; text-align: left; vertical-align: top; overflow-wrap: anywhere; }
            .quantity { width: 14%; text-align: right; } .sku { width: 16%; }
            th.quantity { white-space: nowrap; }
            thead { display: table-header-group; } tr { break-inside: avoid; }
            small { display: block; } @media print { body { margin: 0; } }
            </style></head><body><h1>Packing slip</h1><p>Order <strong>
            """);
        html.Append(E(model.Number)).Append("</strong><br>Ordered: ")
            .Append(E(model.CreatedAt.ToUniversalTime().ToString("yyyy-MM-dd HH:mm 'UTC'", CultureInfo.InvariantCulture)))
            .Append("</p><h2>Ship to</h2><address>");
        var address = model.Address;
        foreach (var value in new[] { address.FullName, address.Line1, address.Line2,
                     address.City, address.Region, address.PostalCode, address.Country })
            if (!string.IsNullOrWhiteSpace(value)) html.Append(E(value)).Append("<br>");
        html.Append("</address><table><thead><tr><th class=\"sku\">SKU</th><th>Item</th><th class=\"quantity\">Ordered</th><th class=\"quantity\">Fulfilled</th><th class=\"quantity\">Remaining</th></tr></thead><tbody>");
        foreach (var line in model.Lines)
            html.Append("<tr><td>").Append(E(line.Sku)).Append("</td><td>")
                .Append(E(line.ProductName)).Append("<small>").Append(E(line.VariantName))
                .Append("</small></td><td class=\"quantity\">").Append(N(line.Ordered))
                .Append("</td><td class=\"quantity\">").Append(N(line.Fulfilled))
                .Append("</td><td class=\"quantity\">").Append(N(line.Remaining)).Append("</td></tr>");
        return html.Append("</tbody></table></body></html>").ToString();
    }
}
