using Agora.Infrastructure.Services;

namespace Agora.Tests.Unit;

public sealed class AccountExportBoundsTests
{
    [Fact]
    public async Task Serializer_accepts_exactly_five_mib_and_rejects_the_next_byte_without_partial_result()
    {
        var empty=Document(""); var baseline=(await AccountExportService.SerializeBoundedAsync(empty)).Length;
        var exact=await AccountExportService.SerializeBoundedAsync(Document(new string('x',AccountExportService.MaximumBytes-baseline)));
        Assert.Equal(AccountExportService.MaximumBytes,exact.Length);
        await Assert.ThrowsAsync<AccountExportTooLargeException>(()=>
            AccountExportService.SerializeBoundedAsync(Document(new string('x',AccountExportService.MaximumBytes-baseline+1))));
    }

    private static AccountExportV1 Document(string name)=>new(1,DateTimeOffset.UnixEpoch,
        new ExportProfile(Guid.Empty,"e@example.test",name,"Customer",DateTimeOffset.UnixEpoch),
        [],[],[],[],[],[],[],[],[],[]);
}
