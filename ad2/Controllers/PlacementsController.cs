using Microsoft.AspNetCore.Mvc;
using System.Collections.Concurrent;

namespace ad2.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class PlacementsController : ControllerBase
    {
        // Хранение в оперативной памяти
        private static ConcurrentDictionary<string, HashSet<string>> _map = new(StringComparer.OrdinalIgnoreCase);

        [HttpPost("upload")]
        public async Task<IActionResult> Upload(IFormFile файл)
        {
            if (файл == null)
                return BadRequest(new { ошибка = "Файл не загружен" });

            var новаяКарта = new ConcurrentDictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
            var ошибки = new List<string>();
            int обработаноСтрок = 0;

            using var reader = new StreamReader(файл.OpenReadStream());
            string? line;
            while ((line = await reader.ReadLineAsync()) != null)
            {
                line = line.Trim();
                if (string.IsNullOrEmpty(line) || line.StartsWith("#"))
                    continue;

                var parts = line.Split(':');
                if (parts.Length != 2)
                {
                    ошибки.Add($"Некорректная строка: {line}");
                    continue;
                }

                var площадка = parts[0].Trim();
                var локации = parts[1].Split(',')
                    .Select(NormalizeLocation)
                    .Where(l => l != null)
                    .ToList();

                if (string.IsNullOrEmpty(площадка) || !локации.Any())
                {
                    ошибки.Add($"Нет данных в строке: {line}");
                    continue;
                }

                foreach (var loc in локации)
                {
                    if (!новаяКарта.ContainsKey(loc!))
                        новаяКарта[loc!] = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                    новаяКарта[loc!].Add(площадка);
                }

                обработаноСтрок++;
            }

            _map = новаяКарта; 

            var предупреждение = обработаноСтрок == 0 ? "Файл не содержит данных" : null;

            return Ok(new
            {
                успех = true,
                обработаноСтрок,
                локаций = _map.Count,
                ошибки,
                предупреждение
            });
        }

        [HttpGet("search")]
        public IActionResult Search([FromQuery(Name = "локация")] string локация)
        {
            if (string.IsNullOrWhiteSpace(локация))
                return BadRequest(new { ошибка = "Локация не указана" });

            локация = NormalizeLocation(локация);
            if (локация == null)
                return BadRequest(new { ошибка = "Некорректный формат локации" });

            var результат = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var prefix in EnumerateAncestors(локация))
            {
                if (_map.TryGetValue(prefix, out var площадки))
                    результат.UnionWith(площадки);
            }

            return Ok(new
            {
                локация,
                площадки = результат.OrderBy(p => p).ToList()
            });
        }

        private static string? NormalizeLocation(string loc)
        {
            if (string.IsNullOrWhiteSpace(loc))
                return null;

            loc = loc.Trim().ToLowerInvariant();
            if (!loc.StartsWith("/"))
                return null;

            while (loc.Contains("//"))
                loc = loc.Replace("//", "/");

            if (loc.Length > 1 && loc.EndsWith("/"))
                loc = loc.TrimEnd('/');

            return loc;
        }

        private static IEnumerable<string> EnumerateAncestors(string location)
        {
            yield return location;
            while (location.Contains("/"))
            {
                var idx = location.LastIndexOf('/');
                if (idx <= 0) break;
                location = location.Substring(0, idx);
                yield return location;
            }
        }
    }
}
