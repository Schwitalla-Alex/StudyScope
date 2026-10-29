using StudyScope.Core.Models;
using System;
using System.Collections.Generic;
using System.Text;
using System.Xml.Linq;

namespace StudyScope.Core.Models;

public class UserProfile
{
    public string Name { get; set; } = "Standard";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public List<DocumentItem> Documents { get; set; } = new();

    // Die profilbezogene KI-Einstellung:
    public AiConfig AiSettings { get; set; } = new();
}