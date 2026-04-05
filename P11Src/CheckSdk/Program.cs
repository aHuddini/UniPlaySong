using System.Reflection;

var asm = Assembly.LoadFrom(@"C:\Users\asad2\.nuget\packages\playnite.sdk\11.0.0-alpha5\lib\net10.0-windows7.0\Playnite.SDK.dll");
var type = asm.GetTypes().FirstOrDefault(t => t.Name == "MenuItemImpl");
if (type == null) { Console.WriteLine("MenuItemImpl not found"); return; }
Console.WriteLine($"Type: {type.FullName}");
Console.WriteLine("--- Constructors ---");
foreach (var ctor in type.GetConstructors())
{
    var ps = ctor.GetParameters();
    Console.WriteLine($"  ({string.Join(", ", ps.Select(p => $"{p.ParameterType.Name} {p.Name}{(p.HasDefaultValue ? $" = {p.DefaultValue}" : "")}"))})");
}
Console.WriteLine("--- Properties ---");
foreach (var p in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
{
    Console.WriteLine($"  {p.PropertyType.Name} {p.Name}");
}
