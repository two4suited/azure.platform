using Orders.Web.Components;

var builder = WebApplication.CreateBuilder(args);
builder.AddServiceDefaults();
builder.Services.AddRazorComponents().AddInteractiveServerComponents();
builder.Services.AddHttpClient("catalog", c => c.BaseAddress = new Uri("https+http://catalog-api"));
builder.Services.AddHttpClient("orders", c => c.BaseAddress = new Uri("https+http://orders-api"));

var app = builder.Build();
app.MapDefaultEndpoints();
if (!app.Environment.IsDevelopment()) app.UseExceptionHandler("/Error", createScopeForErrors: true);
app.UseStaticFiles();
app.UseAntiforgery();
app.MapRazorComponents<App>().AddInteractiveServerRenderMode();
app.Run();
