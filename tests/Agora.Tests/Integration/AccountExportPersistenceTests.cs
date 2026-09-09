using Agora.Domain.Entities;
using Agora.Infrastructure.Persistence;
using Agora.Infrastructure.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using System.Data.Common;

namespace Agora.Tests.Integration;

public sealed class AccountExportPersistenceTests
{
    [Fact]
    public async Task Global_record_budget_accepts_exactly_10000_and_rejects_10001()
    {
        var path=Path.Combine(Path.GetTempPath(),"agora-export-bound-"+Guid.NewGuid().ToString("N")+".db");
        try
        {
            var options=new DbContextOptionsBuilder<AgoraDbContext>().UseSqlite($"Data Source={path};Pooling=False").Options;
            Guid owner;
            await using(var seed=new AgoraDbContext(options))
            {
                await seed.Database.EnsureCreatedAsync(); var customer=new Customer{Email="export-bound@example.test",PasswordHash="hash"};
                seed.Customers.Add(customer); owner=customer.Id;
                seed.CustomerAddresses.AddRange(Enumerable.Range(0,9999).Select(i=>new CustomerAddress{CustomerId=owner,Label="A"+i,
                    Address=new Address{FullName="A",Line1="1",City="C",Region="R",PostalCode="1",Country="US"}}));
                await seed.SaveChangesAsync();
            }
            await using(var exact=new AgoraDbContext(options))
                Assert.NotEmpty((await new AccountExportService(exact,TimeProvider.System).CreateAsync(owner)).Bytes);
            await using(var add=new AgoraDbContext(options))
            {add.CustomerAddresses.Add(new CustomerAddress{CustomerId=owner,Label="overflow",Address=new Address{FullName="A",Line1="1",City="C",Region="R",PostalCode="1",Country="US"}});await add.SaveChangesAsync();}
            await using(var overflow=new AgoraDbContext(options))
                await Assert.ThrowsAsync<AccountExportTooLargeException>(()=>new AccountExportService(overflow,TimeProvider.System).CreateAsync(owner));
        }
        finally{if(File.Exists(path))File.Delete(path);}
    }

    [Fact]
    public async Task Export_keeps_one_snapshot_while_an_independent_insert_waits_to_commit()
    {
        var path=Path.Combine(Path.GetTempPath(),"agora-export-snapshot-"+Guid.NewGuid().ToString("N")+".db");
        var exportPause=new PauseOnAddressCount(); var insertPause=new PauseOnInsert();
        try
        {
            var plain=new DbContextOptionsBuilder<AgoraDbContext>().UseSqlite($"Data Source={path};Pooling=False;Default Timeout=30").Options;
            Guid owner; await using(var seed=new AgoraDbContext(plain)){await seed.Database.EnsureCreatedAsync();var c=new Customer{Email="snapshot@example.test",PasswordHash="h"};seed.Add(c);await seed.SaveChangesAsync();owner=c.Id;}
            var exportOptions=new DbContextOptionsBuilder<AgoraDbContext>().UseSqlite($"Data Source={path};Pooling=False;Default Timeout=30").AddInterceptors(exportPause).Options;
            var writerOptions=new DbContextOptionsBuilder<AgoraDbContext>().UseSqlite($"Data Source={path};Pooling=False;Default Timeout=30").AddInterceptors(insertPause).Options;
            await using var exportDb=new AgoraDbContext(exportOptions); await using var writerDb=new AgoraDbContext(writerOptions);
            var exportTask=new AccountExportService(exportDb,TimeProvider.System).CreateAsync(owner);
            await exportPause.Reached.Task.WaitAsync(TimeSpan.FromSeconds(30));
            writerDb.CustomerAddresses.Add(new CustomerAddress{CustomerId=owner,Label="concurrent",Address=new Address{FullName="A",Line1="1",City="C",Region="R",PostalCode="1",Country="US"}});
            var writeTask=writerDb.SaveChangesAsync(); await insertPause.Reached.Task.WaitAsync(TimeSpan.FromSeconds(30));
            exportPause.Release.TrySetResult(); var export=await exportTask; insertPause.Release.TrySetResult(); await writeTask;
            Assert.DoesNotContain("concurrent",System.Text.Encoding.UTF8.GetString(export.Bytes));
            await using var fresh=new AgoraDbContext(plain); Assert.Single(await fresh.CustomerAddresses.ToArrayAsync());
        }
        finally{exportPause.Release.TrySetResult();insertPause.Release.TrySetResult();if(File.Exists(path))File.Delete(path);}
    }

    private sealed class PauseOnAddressCount:DbCommandInterceptor
    {
        public TaskCompletionSource Reached{get;}=new(TaskCreationOptions.RunContinuationsAsynchronously); public TaskCompletionSource Release{get;}=new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,CommandEventData data,InterceptionResult<DbDataReader> result,CancellationToken ct=default)
        {if(command.CommandText.Contains("CustomerAddresses")&&command.CommandText.Contains("COUNT",StringComparison.OrdinalIgnoreCase)){Reached.TrySetResult();await Release.Task.WaitAsync(ct);}return result;}
    }
    private sealed class PauseOnInsert:DbCommandInterceptor
    {
        public TaskCompletionSource Reached{get;}=new(TaskCreationOptions.RunContinuationsAsynchronously); public TaskCompletionSource Release{get;}=new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,CommandEventData data,InterceptionResult<DbDataReader> result,CancellationToken ct=default)
        {if(command.CommandText.StartsWith("INSERT INTO \"CustomerAddresses\"",StringComparison.Ordinal)){Reached.TrySetResult();await Release.Task.WaitAsync(ct);}return result;}
    }
}
