using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace GMSL;

public class ModInfo
{
    public string Name { get; set; }
    public string ID { get; set; }
    public string Version { get; set; }
    public string Description { get; set; }
    public List<string> Dependencies { get; set; }
    
    [JsonIgnore]public string ModDir => Environment.CurrentDirectory;
}