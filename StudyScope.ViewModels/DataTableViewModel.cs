using System.ComponentModel;
using System.Runtime.CompilerServices;
using StudyScope.Core.Interfaces;
using StudyScope.Core.Models;
using StudyScope.ViewModels.DataTable;
using StudyScope.ViewModels.Mindmap;
using StudyScope.Core.Localization;
using StudyScope.ViewModels.Resources;

namespace StudyScope.ViewModels;

/// <summary>Exportformate einer Datentabelle (PDF läuft über den Druckdialog der Oberfläche).</summary>
public enum DataTableExportFormat
{
    Csv,
    Excel,
    Markdown
}

/// <summary>
/// Steuert den Reiter „Datentabelle“: Tabelle nach Vorlage aus den aktivierten Dokumenten erstellen, mehrere Tabellen
/// je Profil verwalten, sortieren, filtern, Zellen/Zeilen/Spalten bearbeiten (sofort gespeichert), weitere Zeilen per KI
/// ergänzen und als CSV, Excel oder Markdown exportieren.
/// </summary>
public class DataTableViewModel : INotifyPropertyChanged
{
    private readonly IDataTableService _dataTableService;
    private readonly IDocumentService _documentService;
    private CancellationTokenSource? _cts;
    private string _profileName = string.Empty;
    private DataTableLibrary _library = new();

    public event PropertyChangedEventHandler? PropertyChanged;

    public DataTableViewModel(IDataTableService dataTableService, IDocumentService documentService)
    {
        _dataTableService = dataTableService;
        _documentService = documentService;
    }

    // ---------------- Tabellen ----------------

    /// <summary>Alle Tabellen des Profils, neueste zuerst.</summary>
    public IReadOnlyList<DataTableDocument> Tables => _library.Tables.OrderByDescending(t => t.CreatedAt).ToList();

    private DataTableDocument? _selectedTable;
    public DataTableDocument? SelectedTable
    {
        get => _selectedTable;
        private set
        {
            _selectedTable = value;
            _sortColumn = null;
            _sortDescending = false;
            _filterText = string.Empty;
            _exportFilteredOnly = false;
            OnPropertyChanged();
            OnTableContentChanged();
            OnPropertyChanged(nameof(SortColumn));
            OnPropertyChanged(nameof(SortDescending));
            OnPropertyChanged(nameof(FilterText));
            OnPropertyChanged(nameof(HasFilter));
            OnPropertyChanged(nameof(ExportFilteredOnly));
        }
    }

    // ---------------- Formular „Neue Tabelle“ ----------------

    private bool _isFormOpen;
    public bool IsFormOpen { get => _isFormOpen; set { _isFormOpen = value; OnPropertyChanged(); } }

    private DataTableTemplate _template = DataTableTemplate.Terms;
    public DataTableTemplate Template { get => _template; set { _template = value; OnPropertyChanged(); } }

    private string _customInstruction = string.Empty;
    public string CustomInstruction { get => _customInstruction; set { _customInstruction = value; OnPropertyChanged(); } }

    private string _customColumns = string.Empty;
    /// <summary>Gewünschte Spalten, durch Komma, Semikolon oder Zeilenumbruch getrennt (optional).</summary>
    public string CustomColumns { get => _customColumns; set { _customColumns = value; OnPropertyChanged(); } }

    private string _topic = string.Empty;
    /// <summary>Optionales Thema bzw. Suchbegriff zur Eingrenzung.</summary>
    public string Topic { get => _topic; set { _topic = value; OnPropertyChanged(); } }

    private string _customTitle = string.Empty;
    /// <summary>Optionaler eigener Titel (sonst wählt die KI einen).</summary>
    public string CustomTitle { get => _customTitle; set { _customTitle = value; OnPropertyChanged(); } }

    // ---------------- Sortierung und Filter ----------------

    private int? _sortColumn;
    /// <summary>Sortierte Spalte (Index, <see cref="DataTableSorting.SourceColumn"/> für die Quelle) oder null.</summary>
    public int? SortColumn => _sortColumn;

    private bool _sortDescending;
    public bool SortDescending => _sortDescending;

    private string _filterText = string.Empty;
    public string FilterText
    {
        get => _filterText;
        set
        {
            _filterText = value ?? string.Empty;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasFilter));
            OnPropertyChanged(nameof(VisibleRows));
        }
    }

    public bool HasFilter => DataTableSorting.SplitTerms(FilterText).Count > 0;

    private bool _exportFilteredOnly;
    /// <summary>Nur die gefilterten Zeilen exportieren (wirkt nur, solange ein Filter gesetzt ist).</summary>
    public bool ExportFilteredOnly { get => _exportFilteredOnly; set { _exportFilteredOnly = value; OnPropertyChanged(); } }

    /// <summary>Zeilen der Ansicht: gefiltert und sortiert.</summary>
    public IReadOnlyList<DataTableRow> VisibleRows =>
        SelectedTable == null ? Array.Empty<DataTableRow>() : DataTableSorting.Apply(SelectedTable.Rows, FilterText, SortColumn, SortDescending);

    /// <summary>Alle Zeilen in der aktuellen Sortierung (ohne Filter).</summary>
    public IReadOnlyList<DataTableRow> SortedRows =>
        SelectedTable == null ? Array.Empty<DataTableRow>() : DataTableSorting.Apply(SelectedTable.Rows, null, SortColumn, SortDescending);

    /// <summary>Zeilen für den Export: Standard alle in aktueller Sortierung, auf Wunsch nur die gefilterten.</summary>
    public IReadOnlyList<DataTableRow> RowsForExport => ExportFilteredOnly && HasFilter ? VisibleRows : SortedRows;

    /// <summary>Klick auf die Kopfzeile: neue Spalte aufsteigend, dieselbe Spalte wechselt zwischen auf- und absteigend.</summary>
    public void ToggleSort(int column)
    {
        if (_sortColumn == column)
        {
            _sortDescending = !_sortDescending;
        }
        else
        {
            _sortColumn = column;
            _sortDescending = false;
        }

        OnPropertyChanged(nameof(SortColumn));
        OnPropertyChanged(nameof(SortDescending));
        OnPropertyChanged(nameof(VisibleRows));
    }

    public void ClearSort()
    {
        _sortColumn = null;
        _sortDescending = false;
        OnPropertyChanged(nameof(SortColumn));
        OnPropertyChanged(nameof(SortDescending));
        OnPropertyChanged(nameof(VisibleRows));
    }

    // ---------------- Grenzen ----------------

    public int RowCount => SelectedTable?.Rows.Count ?? 0;
    public bool IsAtRowLimit => RowCount >= DataTableDocument.MaxRows;
    public bool CanAddMoreRows => SelectedTable != null && !IsBusy && !IsAtRowLimit;

    public string RowLimitHint => Loc.Format(VmStrings.DataTable_RowLimitHint, ("max", DataTableDocument.MaxRows));

    // ---------------- Fortschritt und Status ----------------

    private bool _isBusy;
    public bool IsBusy
    {
        get => _isBusy;
        private set { _isBusy = value; OnPropertyChanged(); OnPropertyChanged(nameof(CanAddMoreRows)); }
    }

    private bool _isAppending;
    /// <summary>Läuft gerade „Weitere Zeilen ergänzen“ (statt einer neuen Tabelle)?</summary>
    public bool IsAppending { get => _isAppending; private set { _isAppending = value; OnPropertyChanged(); } }

    private string? _statusMessage;
    public string? StatusMessage { get => _statusMessage; private set { _statusMessage = value; OnPropertyChanged(); } }

    private bool _isError;
    public bool IsError { get => _isError; private set { _isError = value; OnPropertyChanged(); } }

    private bool _isWarning;
    public bool IsWarning { get => _isWarning; private set { _isWarning = value; OnPropertyChanged(); } }

    private IReadOnlyList<string> _statusTips = Array.Empty<string>();
    public IReadOnlyList<string> StatusTips { get => _statusTips; private set { _statusTips = value; OnPropertyChanged(); } }

    private string? _statusDetails;
    public string? StatusDetails { get => _statusDetails; private set { _statusDetails = value; OnPropertyChanged(); } }

    public void ShowStatus(string message, bool isError, IReadOnlyList<string>? tips = null, string? details = null, bool isWarning = false)
    {
        IsError = isError;
        IsWarning = !isError && isWarning;
        StatusTips = tips ?? Array.Empty<string>();
        StatusDetails = string.IsNullOrWhiteSpace(details) ? null : details;
        StatusMessage = message;
    }

    public void ClearStatus()
    {
        StatusMessage = null;
        StatusTips = Array.Empty<string>();
        StatusDetails = null;
        IsError = false;
        IsWarning = false;
    }

    public void Cancel() => _cts?.Cancel();

    // ---------------- Laden ----------------

    public async Task LoadForProfileAsync(string profileName)
    {
        Cancel();
        _profileName = profileName;
        ClearStatus();

        _library = await _dataTableService.LoadLibraryAsync(profileName);
        IsFormOpen = _library.Tables.Count == 0;
        OnPropertyChanged(nameof(Tables));
        SelectedTable = Tables.FirstOrDefault();
    }

    // ---------------- Erstellen ----------------

    public async Task GenerateAsync(IEnumerable<DocumentItem> documents)
    {
        if (IsBusy) return;

        var selectedDocs = documents.Where(d => d.IsSelected && !d.IsMissing).ToList();
        if (selectedDocs.Count == 0)
        {
            ShowStatus(VmStrings.DataTable_SelectDocument, isError: true);
            return;
        }

        if (Template == DataTableTemplate.Custom && string.IsNullOrWhiteSpace(CustomInstruction) && string.IsNullOrWhiteSpace(CustomColumns))
        {
            ShowStatus(Loc.Format(VmStrings.DataTable_CustomMissing, ("custom", TemplateTexts.Name(DataTableTemplate.Custom))), isError: true);
            return;
        }

        IsBusy = true;
        IsAppending = false;
        ClearStatus();
        _cts = new CancellationTokenSource();
        var token = _cts.Token;

        try
        {
            var chunks = await LoadChunksAsync(selectedDocs);
            var fallbackTitle = selectedDocs.Count == 1
                ? $"{DataTableTemplates.DisplayName(Template)}: {Path.GetFileNameWithoutExtension(selectedDocs[0].FileName)}"
                : $"{DataTableTemplates.DisplayName(Template)}: {_profileName}";
            var columns = ParseColumns(CustomColumns);

            var result = await _dataTableService.GenerateAsync(chunks, Template, CustomInstruction, columns, Topic, fallbackTitle, token);
            if (token.IsCancellationRequested)
            {
                ShowStatus(VmStrings.DataTable_Cancelled, isError: false);
                return;
            }

            if (!result.IsSuccess)
            {
                var failure = MindmapStatusTexts.ForFailure(result.FailureReason, result.RawResponse, GenerationSubject.DataTable, VmStrings.DataTable_Generate);
                ShowStatus(failure.Message, isError: true, failure.Tips, failure.Details);
                return;
            }

            var table = result.Table!;
            if (!string.IsNullOrWhiteSpace(CustomTitle))
                table.Title = CustomTitle.Trim();
            table.CreatedAt = DateTime.Now;
            table.SourceDocuments = selectedDocs.Select(d => d.FileName).ToList();
            if (table.Rows.Count > DataTableDocument.MaxRows)
                table.Rows = table.Rows.Take(DataTableDocument.MaxRows).ToList();

            _library.Tables.Add(table);
            await SaveAsync();

            CustomTitle = string.Empty;
            IsFormOpen = false;
            OnPropertyChanged(nameof(Tables));
            SelectedTable = table;

            if (result.WasRepaired)
                ShowStatus(Loc.Format(VmStrings.DataTable_CreatedRepaired, ("count", table.Rows.Count)), isError: false,
                    new[] { Loc.Format(VmStrings.DataTable_TipCompleteWithMore, ("button", VmStrings.DataTable_AddMoreRows)), VmStrings.DataTable_TipTopicShorter }, isWarning: true);
            else
                ShowStatus(Loc.Format(VmStrings.DataTable_Created, ("title", table.Title), ("rows", table.Rows.Count), ("columns", table.Columns.Count)), isError: false);
        }
        catch (OperationCanceledException)
        {
            ShowStatus(VmStrings.DataTable_Cancelled, isError: false);
        }
        catch (Exception ex)
        {
            ShowStatus(Loc.Format(VmStrings.DataTable_GenerateFailed, ("error", ex.Message)), isError: true);
        }
        finally
        {
            _cts?.Dispose();
            _cts = null;
            IsBusy = false;
        }
    }

    /// <summary>
    /// Lässt die KI weitere Zeilen suchen – bevorzugt in den Quelldokumenten der Tabelle, sonst in den aktivierten Dokumenten.
    /// Wiederholungen werden verworfen, die Grenze von <see cref="DataTableDocument.MaxRows"/> Zeilen wird eingehalten.
    /// </summary>
    public async Task GenerateMoreRowsAsync(IEnumerable<DocumentItem> documents)
    {
        var table = SelectedTable;
        if (IsBusy || table == null) return;

        if (IsAtRowLimit)
        {
            ShowStatus($"⚠️ {RowLimitHint}", isError: false, isWarning: true);
            return;
        }

        var all = documents.Where(d => !d.IsMissing).ToList();
        var docs = all.Where(d => table.SourceDocuments.Contains(d.FileName, StringComparer.OrdinalIgnoreCase)).ToList();
        if (docs.Count == 0)
            docs = all.Where(d => d.IsSelected).ToList();
        if (docs.Count == 0)
        {
            ShowStatus(VmStrings.DataTable_SourcesMissing, isError: true);
            return;
        }

        IsBusy = true;
        IsAppending = true;
        ClearStatus();
        _cts = new CancellationTokenSource();
        var token = _cts.Token;

        try
        {
            var chunks = await LoadChunksAsync(docs);
            var remaining = DataTableDocument.MaxRows - table.Rows.Count;
            var result = await _dataTableService.GenerateMoreRowsAsync(table, chunks, remaining, token);
            if (token.IsCancellationRequested)
            {
                ShowStatus(VmStrings.DataTable_AppendCancelled, isError: false);
                return;
            }

            if (result.FailureReason != MindmapFailureReason.None)
            {
                var failure = MindmapStatusTexts.ForFailure(result.FailureReason, result.RawResponse, GenerationSubject.NewRows, VmStrings.DataTable_AddMoreRows);
                ShowStatus(failure.Message, isError: true, failure.Tips, failure.Details);
                return;
            }

            table.AppendCount++;
            foreach (var doc in docs.Where(d => !table.SourceDocuments.Contains(d.FileName, StringComparer.OrdinalIgnoreCase)))
                table.SourceDocuments.Add(doc.FileName);

            if (result.Rows.Count == 0)
            {
                await SaveAsync();
                ShowStatus(result.DuplicatesSkipped > 0
                        ? Loc.Format(VmStrings.DataTable_OnlyDuplicates, ("count", result.DuplicatesSkipped))
                        : VmStrings.DataTable_NoNewRows,
                    isError: false,
                    new[] { VmStrings.DataTable_TipRetryOtherExcerpt, VmStrings.DataTable_TipMoreDocuments },
                    isWarning: true);
                return;
            }

            table.Rows.AddRange(result.Rows);
            await SaveAsync();
            OnTableContentChanged();

            var message = result.DuplicatesSkipped > 0
                ? Loc.Format(VmStrings.DataTable_RowsAddedWithDuplicates, ("count", result.Rows.Count), ("duplicates", result.DuplicatesSkipped))
                : Loc.Format(VmStrings.DataTable_RowsAdded, ("count", result.Rows.Count));
            if (IsAtRowLimit)
                ShowStatus(message + " " + RowLimitHint, isError: false, isWarning: true);
            else
                ShowStatus(message, isError: false);
        }
        catch (OperationCanceledException)
        {
            ShowStatus(VmStrings.DataTable_AppendCancelled, isError: false);
        }
        catch (Exception ex)
        {
            ShowStatus(Loc.Format(VmStrings.DataTable_AppendFailed, ("error", ex.Message)), isError: true);
        }
        finally
        {
            _cts?.Dispose();
            _cts = null;
            IsBusy = false;
            IsAppending = false;
        }
    }

    private async Task<List<SourceChunk>> LoadChunksAsync(IEnumerable<DocumentItem> documents)
    {
        var chunks = new List<SourceChunk>();
        foreach (var doc in documents)
        {
            foreach (var text in await _documentService.ExtractChunksAsync(doc.FilePath))
                chunks.Add(new SourceChunk(doc.FileName, text));
        }
        return chunks;
    }

    /// <summary>Zerlegt die Spaltenliste des Formulars (Komma, Semikolon, senkrechter Strich oder Zeilenumbruch).</summary>
    public static List<string> ParseColumns(string? text)
    {
        var result = new List<string>();
        foreach (var part in (text ?? string.Empty).Split(new[] { ',', ';', '|', '\n' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (part.Length > 0 && !result.Contains(part, StringComparer.OrdinalIgnoreCase) &&
                !DataTableDocument.IsReservedColumnName(part))
                result.Add(part);
        }
        return result.Take(DataTableDocument.MaxColumns).ToList();
    }

    // ---------------- Tabellen verwalten ----------------

    public void SelectTable(string tableId)
    {
        SelectedTable = _library.Tables.FirstOrDefault(t => t.Id == tableId);
        IsFormOpen = false;
    }

    public void OpenForm()
    {
        IsFormOpen = true;
        ClearStatus();
    }

    public async Task<bool> RenameTableAsync(string tableId, string newTitle)
    {
        var table = _library.Tables.FirstOrDefault(t => t.Id == tableId);
        var title = (newTitle ?? string.Empty).Trim();
        if (table == null || title.Length == 0)
            return false;

        table.Title = title;
        await SaveAsync();
        OnPropertyChanged(nameof(Tables));
        if (SelectedTable?.Id == tableId) OnPropertyChanged(nameof(SelectedTable));
        return true;
    }

    public async Task DeleteTableAsync(string tableId)
    {
        var table = _library.Tables.FirstOrDefault(t => t.Id == tableId);
        if (table == null) return;

        _library.Tables.Remove(table);
        await SaveAsync();
        OnPropertyChanged(nameof(Tables));

        if (SelectedTable?.Id == tableId)
            SelectedTable = Tables.FirstOrDefault();
        if (_library.Tables.Count == 0)
            IsFormOpen = true;

        ShowStatus(Loc.Format(VmStrings.DataTable_Deleted, ("title", table.Title)), isError: false);
    }

    // ---------------- Bearbeiten ----------------

    /// <summary>Setzt den Text einer Zelle; <see cref="DataTableSorting.SourceColumn"/> ändert die Quelle.</summary>
    public async Task<bool> SetCellAsync(string rowId, int column, string value)
    {
        var row = SelectedTable?.Rows.FirstOrDefault(r => r.Id == rowId);
        if (row == null) return false;

        var text = (value ?? string.Empty).Replace("\r", string.Empty).Trim();
        if (column == DataTableSorting.SourceColumn)
        {
            if (row.Source == text) return true;
            row.Source = text;
        }
        else
        {
            if (column < 0 || column >= SelectedTable!.Columns.Count) return false;
            while (row.Cells.Count < SelectedTable.Columns.Count) row.Cells.Add(string.Empty);
            if (row.Cells[column] == text) return true;
            row.Cells[column] = text;
        }

        await SaveAsync();
        OnTableContentChanged();
        return true;
    }

    /// <summary>Fügt eine leere Zeile am Ende an und liefert ihre Id (null an der Zeilengrenze).</summary>
    public async Task<string?> AddRowAsync()
    {
        var table = SelectedTable;
        if (table == null) return null;

        if (IsAtRowLimit)
        {
            ShowStatus($"⚠️ {RowLimitHint}", isError: false, isWarning: true);
            return null;
        }

        var row = new DataTableRow { Cells = table.Columns.Select(_ => string.Empty).ToList() };
        table.Rows.Add(row);
        await SaveAsync();
        OnTableContentChanged();
        return row.Id;
    }

    public async Task DeleteRowAsync(string rowId)
    {
        var table = SelectedTable;
        var row = table?.Rows.FirstOrDefault(r => r.Id == rowId);
        if (table == null || row == null) return;

        table.Rows.Remove(row);
        await SaveAsync();
        OnTableContentChanged();
    }

    /// <summary>Höchstlänge eines Spaltennamens.</summary>
    public const int MaxColumnNameLength = 60;

    /// <summary>Prüft einen Spaltennamen; liefert eine Fehlermeldung oder null.</summary>
    public string? ValidateColumnName(string? name, int? exceptColumn = null)
    {
        var n = (name ?? string.Empty).Trim();
        if (n.Length == 0)
            return VmStrings.DataTable_ColumnNameEmpty;
        if (n.Length > MaxColumnNameLength)
            return Loc.Format(VmStrings.DataTable_ColumnNameTooLong, ("max", MaxColumnNameLength));
        if (DataTableDocument.IsReservedColumnName(n))
            return Loc.Format(VmStrings.DataTable_ColumnNameReserved, ("name", n));
        if (SelectedTable != null && SelectedTable.Columns.Where((_, i) => i != exceptColumn).Any(c => string.Equals(c, n, StringComparison.OrdinalIgnoreCase)))
            return Loc.Format(VmStrings.DataTable_ColumnNameExists, ("name", n));
        return null;
    }

    public async Task<bool> RenameColumnAsync(int column, string newName)
    {
        var table = SelectedTable;
        if (table == null || column < 0 || column >= table.Columns.Count) return false;

        var error = ValidateColumnName(newName, column);
        if (error != null)
        {
            ShowStatus($"⚠️ {error}", isError: true);
            return false;
        }

        table.Columns[column] = newName.Trim();
        await SaveAsync();
        OnTableContentChanged();
        return true;
    }

    public async Task<bool> AddColumnAsync(string name)
    {
        var table = SelectedTable;
        if (table == null) return false;

        if (table.Columns.Count >= DataTableDocument.MaxColumns)
        {
            ShowStatus(Loc.Format(VmStrings.DataTable_MaxColumns, ("max", DataTableDocument.MaxColumns)), isError: true);
            return false;
        }

        var error = ValidateColumnName(name);
        if (error != null)
        {
            ShowStatus($"⚠️ {error}", isError: true);
            return false;
        }

        table.Columns.Add(name.Trim());
        foreach (var row in table.Rows)
        {
            while (row.Cells.Count < table.Columns.Count) row.Cells.Add(string.Empty);
        }

        await SaveAsync();
        OnTableContentChanged();
        return true;
    }

    public async Task<bool> DeleteColumnAsync(int column)
    {
        var table = SelectedTable;
        if (table == null || column < 0 || column >= table.Columns.Count) return false;

        if (table.Columns.Count <= 1)
        {
            ShowStatus(VmStrings.DataTable_LastColumn, isError: true);
            return false;
        }

        table.Columns.RemoveAt(column);
        foreach (var row in table.Rows.Where(r => column < r.Cells.Count))
            row.Cells.RemoveAt(column);

        // Sortierung an die verschobenen Spalten anpassen
        if (_sortColumn == column)
        {
            _sortColumn = null;
            _sortDescending = false;
        }
        else if (_sortColumn > column)
        {
            _sortColumn--;
        }

        await SaveAsync();
        OnPropertyChanged(nameof(SortColumn));
        OnPropertyChanged(nameof(SortDescending));
        OnTableContentChanged();
        return true;
    }

    private Task SaveAsync() => _dataTableService.SaveLibraryAsync(_profileName, _library);

    private void OnTableContentChanged()
    {
        OnPropertyChanged(nameof(VisibleRows));
        OnPropertyChanged(nameof(RowCount));
        OnPropertyChanged(nameof(IsAtRowLimit));
        OnPropertyChanged(nameof(CanAddMoreRows));
    }

    // ---------------- Export ----------------

    public static string Extension(DataTableExportFormat format) => format switch
    {
        DataTableExportFormat.Csv => "csv",
        DataTableExportFormat.Excel => "xlsx",
        _ => "md"
    };

    public static string FormatDescription(DataTableExportFormat format) => format switch
    {
        DataTableExportFormat.Csv => VmStrings.DataTable_FormatCsv,
        DataTableExportFormat.Excel => VmStrings.DataTable_FormatExcel,
        _ => VmStrings.DataTable_FormatMarkdown
    };

    public async Task<bool> ExportAsync(DataTableExportFormat format, string filePath)
    {
        var table = SelectedTable;
        if (table == null) return false;

        try
        {
            var rows = RowsForExport;
            switch (format)
            {
                case DataTableExportFormat.Csv:
                    await File.WriteAllBytesAsync(filePath, _dataTableService.ExportToCsv(table, rows, ExportCulture.Current));
                    break;
                case DataTableExportFormat.Excel:
                    await _dataTableService.ExportToXlsxAsync(table, rows, filePath, ExportCulture.Current);
                    break;
                default:
                    await File.WriteAllTextAsync(filePath, _dataTableService.ExportToMarkdown(table, rows, ExportCulture.Current));
                    break;
            }

            var message = rows.Count == table.Rows.Count
                ? Loc.Format(VmStrings.DataTable_Exported, ("format", FormatDescription(format)), ("file", Path.GetFileName(filePath)), ("rows", rows.Count))
                : Loc.Format(VmStrings.DataTable_ExportedFiltered, ("format", FormatDescription(format)), ("file", Path.GetFileName(filePath)), ("rows", rows.Count), ("total", table.Rows.Count));
            ShowStatus(message, isError: false);
            return true;
        }
        catch (Exception ex)
        {
            ShowStatus(Loc.Format(VmStrings.DataTable_ExportFailed, ("error", ex.Message)), isError: true,
                new[] { VmStrings.DataTable_TipFileOpenExcel });
            return false;
        }
    }

    /// <summary>Dateiname aus dem Tabellentitel ohne unzulässige Zeichen.</summary>
    public string SuggestFileName(DataTableExportFormat format)
    {
        var name = string.IsNullOrWhiteSpace(SelectedTable?.Title) ? "Datentabelle" : SelectedTable!.Title;
        foreach (var c in Path.GetInvalidFileNameChars().Concat(new[] { '/', '\\', ':', '*', '?', '"', '<', '>', '|' }))
            name = name.Replace(c, '_');
        name = name.Length > 60 ? name[..60] : name;
        return $"{name.Trim()}_{DateTime.Now:yyyyMMdd}.{Extension(format)}";
    }

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
