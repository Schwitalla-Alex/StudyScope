// Alle Tests laufen mit deutscher Oberflächensprache (Original der Texte), unabhängig von der Sprache des Rechners
// bzw. des CI-Runners. Tests für andere Sprachen setzen [SetUICulture] am Test selbst.
// Die Formatkultur (Zahlen, Datum) bleibt die des Rechners – so fallen kulturabhängige Fehler in der CI auf.
[assembly: SetUICulture("de-DE")]
