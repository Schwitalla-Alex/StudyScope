using StudyScope.Core.Models;
using System;
using System.Collections.Generic;
using System.Text;
using System.Xml.Linq;

namespace StudyScope.Core.Models;

public class UserProfile
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "Standard";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public List<DocumentItem> Documents { get; set; } = new();
}