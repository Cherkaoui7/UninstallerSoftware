using System.Collections.Generic;

namespace Uninstaller.Domain.Entities;

public class PathSafetyResult
{
    public bool IsValid { get; set; }
    public bool IsCanonical { get; set; }
    public bool IsProtected { get; set; }
    public bool IsReparsePoint { get; set; }
    public bool IsWithinExpectedRoot { get; set; }
    public string CanonicalPath { get; set; }
    public string Reason { get; set; }
    public List<string> Warnings { get; set; } = new List<string>();
}
