using StudyScope.Core.Models;
using StudyScope.ViewModels.Resources;

namespace StudyScope.ViewModels;

/// <summary>
/// Übersetzte Anzeigenamen, Beschreibungen und Spaltenbeispiele der Vorlagen von Datentabelle und Bericht.
/// <see cref="DataTableTemplates"/> und <see cref="ReportTemplates"/> in Core bleiben deutsch, weil Prompts und
/// Exporte sie verwenden; die Oberfläche zeigt nur diese Texte.
/// </summary>
public static class TemplateTexts
{
    public static string Name(DataTableTemplate template) => template switch
    {
        DataTableTemplate.Terms => VmStrings.DtTemplate_Terms,
        DataTableTemplate.Comparison => VmStrings.DtTemplate_Comparison,
        DataTableTemplate.Timeline => VmStrings.DtTemplate_Timeline,
        DataTableTemplate.People => VmStrings.DtTemplate_People,
        DataTableTemplate.Formulas => VmStrings.DtTemplate_Formulas,
        _ => VmStrings.DtTemplate_Custom
    };

    public static string Description(DataTableTemplate template) => template switch
    {
        DataTableTemplate.Terms => VmStrings.DtTemplate_Terms_Desc,
        DataTableTemplate.Comparison => VmStrings.DtTemplate_Comparison_Desc,
        DataTableTemplate.Timeline => VmStrings.DtTemplate_Timeline_Desc,
        DataTableTemplate.People => VmStrings.DtTemplate_People_Desc,
        DataTableTemplate.Formulas => VmStrings.DtTemplate_Formulas_Desc,
        _ => VmStrings.DtTemplate_Custom_Desc
    };

    /// <summary>
    /// Spaltenvorschlag als Beispiel für die Oberfläche („Begriff, Definition, Beispiel“), leer bei eigener Vorgabe.
    /// Die KI bekommt unabhängig davon <see cref="DataTableTemplates.SuggestedColumns"/>.
    /// </summary>
    public static string SuggestedColumns(DataTableTemplate template) => template switch
    {
        DataTableTemplate.Terms => VmStrings.DtTemplate_Terms_Columns,
        DataTableTemplate.Comparison => VmStrings.DtTemplate_Comparison_Columns,
        DataTableTemplate.Timeline => VmStrings.DtTemplate_Timeline_Columns,
        DataTableTemplate.People => VmStrings.DtTemplate_People_Columns,
        DataTableTemplate.Formulas => VmStrings.DtTemplate_Formulas_Columns,
        _ => string.Empty
    };

    public static string Name(ReportTemplate template) => template switch
    {
        ReportTemplate.StudyGuide => VmStrings.ReportTemplate_StudyGuide,
        ReportTemplate.Briefing => VmStrings.ReportTemplate_Briefing,
        ReportTemplate.Glossary => VmStrings.ReportTemplate_Glossary,
        ReportTemplate.Faq => VmStrings.ReportTemplate_Faq,
        _ => VmStrings.ReportTemplate_Custom
    };

    public static string Description(ReportTemplate template) => template switch
    {
        ReportTemplate.StudyGuide => VmStrings.ReportTemplate_StudyGuide_Desc,
        ReportTemplate.Briefing => VmStrings.ReportTemplate_Briefing_Desc,
        ReportTemplate.Glossary => VmStrings.ReportTemplate_Glossary_Desc,
        ReportTemplate.Faq => VmStrings.ReportTemplate_Faq_Desc,
        _ => VmStrings.ReportTemplate_Custom_Desc
    };
}
