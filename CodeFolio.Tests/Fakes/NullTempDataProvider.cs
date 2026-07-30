using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.ViewFeatures;

namespace CodeFolio.Tests.Fakes;

/// <summary>No-op ITempDataProvider — lets a controller's TempData be assigned outside the real MVC pipeline.</summary>
public class NullTempDataProvider : ITempDataProvider
{
    public IDictionary<string, object> LoadTempData(HttpContext context) => new Dictionary<string, object>();
    public void SaveTempData(HttpContext context, IDictionary<string, object> values) { }
}
