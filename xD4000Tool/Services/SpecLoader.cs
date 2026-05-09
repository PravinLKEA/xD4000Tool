using System;
using System.IO;
using System.Text.Json;
using xD4000Tool.Models;

namespace xD4000Tool.Services;

public static class SpecLoader
{
    public static SpecModel Load()
    {
        var path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets", "xd4000_spec.json");
        var json = File.ReadAllText(path);
        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        var root = JsonSerializer.Deserialize<JsonElement>(json, options);

        var model = new SpecModel();
        if (root.TryGetProperty("version", out var v)) model.Version = v.GetString() ?? "";

        if (root.TryGetProperty("parameters", out var pArr))
        {
            foreach (var p in pArr.EnumerateArray())
            {
                model.Parameters.Add(new ParameterDefinition
                {
                    Code = p.GetProperty("code").GetString() ?? "",
                    Name = p.GetProperty("name").GetString() ?? "",
                    Address = p.GetProperty("addr").GetInt32(),
                    Category = p.GetProperty("category").GetString() ?? "",
                    Menu = p.GetProperty("menu").GetString() ?? "",
                    Access = p.GetProperty("access").GetString() ?? "",
                    Type = p.GetProperty("type").GetString() ?? "",
                    Units = p.GetProperty("units").GetString() ?? "",
                    Factory = p.GetProperty("factory").GetString() ?? "",
                    Range = p.GetProperty("range").GetString() ?? "",
                });
            }
        }

        if (root.TryGetProperty("registerBits", out var rb))
        {
            foreach (var prop in rb.EnumerateObject())
            {
                var list = new System.Collections.Generic.List<BitItem>();
                foreach (var item in prop.Value.EnumerateArray())
                {
                    list.Add(new BitItem
                    {
                        Bit = item.GetProperty("bit").GetInt32(),
                        Text = item.GetProperty("text").GetString() ?? "",
                    });
                }
                model.RegisterBits[prop.Name] = list;
            }
        }

        return model;
    }
}
