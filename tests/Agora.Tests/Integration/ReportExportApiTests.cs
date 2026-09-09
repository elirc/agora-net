using System.Net;using System.Net.Http.Json;using Agora.Api.Contracts;using Agora.Domain.Entities;using Agora.Infrastructure.Services;using Microsoft.Extensions.DependencyInjection;
namespace Agora.Tests.Integration;
public sealed class ReportExportApiTests
{
 [Fact]public async Task Queue_run_poll_and_download_are_separate_owned_steps()
 {using var scenario=await ReportTestScenario.Create();var now=scenario.Clock.GetUtcNow();var queued=await Queue(scenario.Admin,now.AddDays(-1),now.AddDays(1));Assert.Equal("Queued",queued.Status);Assert.Equal(HttpStatusCode.Conflict,(await scenario.Admin.GetAsync($"/api/admin/report-exports/{queued.Id}/download")).StatusCode);var runner=new ReportExportRunner(scenario.App.Services.GetRequiredService<IServiceScopeFactory>(),scenario.Clock);await runner.RunOnceAsync();var status=await scenario.Admin.GetFromJsonAsync<ReportExportResponse>($"/api/admin/report-exports/{queued.Id}");Assert.Equal("Succeeded",status!.Status);var download=await scenario.Admin.GetAsync($"/api/admin/report-exports/{queued.Id}/download");Assert.Equal(HttpStatusCode.OK,download.StatusCode);Assert.Equal("text/csv",download.Content.Headers.ContentType!.MediaType);Assert.StartsWith("orderNumber,paidAt,status,currency",await download.Content.ReadAsStringAsync());scenario.Clock.Instant=now.AddHours(25);Assert.Equal(HttpStatusCode.Gone,(await scenario.Admin.GetAsync($"/api/admin/report-exports/{queued.Id}/download")).StatusCode);Assert.Equal(1,await runner.CleanupAsync());Assert.Equal("Succeeded",(await scenario.Admin.GetFromJsonAsync<ReportExportResponse>($"/api/admin/report-exports/{queued.Id}"))!.Status);}
 [Fact]public async Task Cancelled_job_never_publishes_and_active_cap_is_ten()
 {using var scenario=await ReportTestScenario.Create();var now=scenario.Clock.GetUtcNow();var first=await Queue(scenario.Admin,now.AddDays(-1),now);var cancelled=await scenario.Admin.PostAsync($"/api/admin/report-exports/{first.Id}/cancel",null);Assert.Equal(HttpStatusCode.OK,cancelled.StatusCode);await new ReportExportRunner(scenario.App.Services.GetRequiredService<IServiceScopeFactory>(),scenario.Clock).RunOnceAsync();Assert.Equal(HttpStatusCode.Conflict,(await scenario.Admin.GetAsync($"/api/admin/report-exports/{first.Id}/download")).StatusCode);for(var i=0;i<10;i++)await Queue(scenario.Admin,now.AddDays(-1),now);Assert.False((await scenario.Admin.PostAsJsonAsync("/api/admin/report-exports",new CreateReportExportRequest(now.AddDays(-1),now))).IsSuccessStatusCode);}

 [Fact]
 public async Task Another_admin_cannot_observe_cancel_or_download_an_owned_job()
 {
     using var scenario = await ReportTestScenario.Create();
     var now = scenario.Clock.GetUtcNow();
     var job = await Queue(scenario.Admin, now.AddDays(-1), now);
     using var other = scenario.App.CreateClient();
     var email = $"report-admin-{Guid.NewGuid():N}@example.test";
     await TestAuth.RegisterAsync(other, email);
     await scenario.Db(async db =>
     {
         var account = await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions
             .SingleAsync(db.Customers.Where(x => x.Email == email));
         account.Role = CustomerRole.Admin;
         await db.SaveChangesAsync();
     });
     other.UseBearer(await TestAuth.LoginAsync(other, email, TestAuth.CustomerPassword));

     Assert.Equal(HttpStatusCode.NotFound, (await other.GetAsync($"/api/admin/report-exports/{job.Id}")).StatusCode);
     Assert.Equal(HttpStatusCode.NotFound, (await other.PostAsync($"/api/admin/report-exports/{job.Id}/cancel", null)).StatusCode);
     Assert.Equal(HttpStatusCode.NotFound, (await other.GetAsync($"/api/admin/report-exports/{job.Id}/download")).StatusCode);
 }
 private static async Task<ReportExportResponse>Queue(HttpClient c,DateTimeOffset from,DateTimeOffset to){var r=await c.PostAsJsonAsync("/api/admin/report-exports",new CreateReportExportRequest(from,to));r.EnsureSuccessStatusCode();return(await r.Content.ReadFromJsonAsync<ReportExportResponse>())!;}
}
