# Store-Eintrag — Hotline AI (Deutsch)

Model-drafted translation of `store/listing.md`; have a native speaker review it before publishing. Paste into Partner
Center › the submission › Store listings › Deutsch. Product name stays "Hotline AI".

## Kurzbeschreibung

Nutze die Copilot-Taste als natives KI-Chatfenster. Bring deine Claude-, Codex- oder Gemini-Anmeldung, deinen API-Schlüssel oder ein lokales Modell mit. Ordne die Leiste, füge eigene Buttons hinzu und nimm den Chat in eine andere App mit, wenn du mehr Platz brauchst.

## Beschreibung

Ich habe das gebaut, weil die Copilot-Taste für mich nichts Nützliches tut. Hotline AI macht daraus ein natives
KI-Chatfenster, das mit der KI deiner Wahl spricht.

Drück die Taste, und auf dem Monitor, an dem du arbeitest, öffnet sich ein Panel, bereit zum Tippen. Stell eine Frage,
die Antwort erscheint live; drück die Taste erneut, und es ist weg. Kein Browser-Tab, kein Konto bei uns.

NUTZE DIE KI, DIE DU SCHON HAST
• Claude Code: nutzt dein installiertes Claude Code und dessen Anmeldung, dein Claude-Abo funktioniert also ohne API-Schlüssel.
• OpenAI Codex CLI: nutzt dein installiertes Codex und dessen Anmeldung, dein ChatGPT-Abo funktioniert also ohne API-Schlüssel.
• Antigravity CLI (agy): Gemini mit deiner Google-Anmeldung.
• Dein eigener API-Schlüssel: Anthropic, Google Gemini, OpenAI und andere OpenAI-kompatible Dienste wie OpenRouter.
• Lokale Modelle: jeder Server im OpenAI-Stil auf deinem PC (z. B. llama.cpp oder LM Studio), sodass der Chat auf deinem
  Computer bleibt.
Modell, Anbieter und Denkaufwand wählst du direkt in der Leiste des Panels.
Du bringst den Dienst mit oder betreibst das Modell selbst. Hotline enthält kein KI-Abonnement.

KONTEXT KOMMT MIT
• In einer anderen App markierter Text wird beim Drücken der Taste automatisch angehängt; du kannst ihn vor dem Senden
  entfernen.
• Nimm das Fenster auf, in dem du warst, den ganzen Bildschirm oder einen gezogenen Bereich; füge Bilder und Dateien ein
  oder zieh sie hinein.
• Modelle, die keine Bilder sehen, bekommen stattdessen den Text deines Screenshots, gelesen auf deinem PC mit der OCR
  von Windows.

FÜR SCHNELLE AUFGABEN GEMACHT
• Schnellaktionen: Tippe / für Übersetzen, Zusammenfassen, Korrigieren, Erklären, /reply, /shorter, /formal oder /bullets,
  oder schreib eigene als einfache Textdateien.
• Gedächtnis: „/remember Ich bevorzuge metrische Einheiten“, und jeder Chat weiß es, egal welche KI du nutzt.
• Sprache: Lege langes Drücken auf Spracheingabe fest, halte die Taste und sprich; loslassen zum Beenden. Dies nutzt die
  Online-Spracherkennung von Windows.
• Letzte Chats sind einen Klick entfernt, in einer durchsuchbaren Liste. Ctrl+H oder /resume öffnet sie ebenfalls. CLI-Chats
  bleiben im eigenen Verlauf der CLI und können nach einem Neustart fortgesetzt werden.
• Click to Do und Windows-Aktionen: Text oder ein Bild aus anderen Apps an Hotline AI senden.
• Weiterführen in: Nimm den Chat mit in ein Terminal, Open WebUI, die llama.cpp-Weboberfläche, LM Studio oder dein eigenes
  Web- oder App-Ziel. Open WebUI erhält einen echten Chat. LM Studio öffnet sich mit dem kopierten Chat zum Einfügen;
  andere Ziele variieren.
• Solange das Panel den Fokus hat, nutze Tastenkombinationen für neuen Chat, letzte Chats, Einstellungen, Anheften, letzte
  Antwort kopieren, Aufnahmen, Prompts sowie die Auswahl von Modell, Anbieter und Denkaufwand. Auch deine eigenen
  Schaltflächen können eine haben. Sie benötigen Ctrl, Alt oder Win, damit normales Tippen nie beeinträchtigt wird. Ändere
  sie in den Einstellungen oder in toolbar.json.
• Erhalte eine Windows-Benachrichtigung, wenn eine Antwort fertiggestellt wird, während du in einer anderen App bist.

NATIV, LEICHT UND DEINS
• Mit WinUI 3 gebaut: öffnet schnell, folgt deinem hellen oder dunklen Design, kein eingebetteter Browser. Nativ auf x64
  und ARM64.
• Wähle eine Akzentfarbe oder nutze die von Windows. Es gibt ein Design für hohen Kontrast, und Hotline folgt den
  Kontrast-Designs und der Transparenzeinstellung von Windows.
• Reichhaltige Antworten: Überschriften, Listen, Tabellen, Code mit Kopieren-Schaltfläche und seitlichem Scrollen,
  Formeln und einfache Zeichnungen. Das Panel folgt Antworten, während sie gestreamt werden.
• Halte Alt gedrückt, um das Panel zu verschieben oder in der Größe anzupassen. Hefte es an, wähle, ob der Chat nach oben
  oder unten wächst, und lege Töne für das Öffnen/Schließen fest. Fensterbreiten nutzen echte Pixel. Du kannst ein schmales
  Panel öffnen lassen, das breiter wird, sobald ein Chat beginnt.
• Alles ist eine einfache Datei, die du bearbeiten kannst: Einstellungen, Verbindungen, Prompts, die Schaltflächen in der
  Leiste.
• Schaltflächenzeilen v2: Ziehe Schaltflächen in Einstellungen > Fenster > Schaltflächenzeilen zwischen Zeilen hin und her
  oder bearbeite toolbar.json. Füge Schaltflächen für Schnellaktionen oder vorgefertigten Text mit deinem eigenen PNG-Bild
  hinzu. Wähle Breiten in Zellen, passe die Größe jedes Auswahlfelds separat an und lege weniger genutzte Schaltflächen in
  ein Mehr-Menü. Eine schmale Leiste verschiebt Schaltflächen, die nicht hineinpassen, in ein Menü.
• Ein fehlerhafter Einstellungswert fällt mit einer Warnung auf den Standard zurück; die übrigen Einstellungen gelten
  weiterhin. Vor dem Ersetzen einer manuell bearbeiteten Einstellungs-, Verbindungs- oder Symbolleistendatei erstellt
  Hotline ein Backup. Schlägt dieses Backup fehl, bleibt die Datei unberührt. Beim Ersetzen der eigenen letzten Speicherung
  wird kein weiteres Backup erstellt.
• Einstellungen > Backup exportiert und importiert Einstellungen, Schaltflächenzeilen und Bilder, MCP-Tools, Töne,
  Verbindungen, Schnellaktionen und Prompts. Der Import sichert zuerst jede Datei, die er ersetzt; ohne Backup kein
  Schreiben. API-Schlüssel werden niemals exportiert – trage sie auf dem anderen PC erneut ein. Verlauf und Gedächtnis sind
  nicht im Paket enthalten; MCP-Umgebungswerte schon, halte das Paket also privat, wenn du dort Geheimnisse hinterlegt hast.
• Tools, mit Bedacht: API-Verbindungen können Tools deiner MCP-Server nutzen; alles, was etwas ändert, fragt dich zuerst,
  es sei denn, du hast es erlaubt. Schreibgeschützte Tools werden ohne Nachfrage ausgeführt, basierend auf den Angaben des
  Servers.

PRIVAT VON GRUND AUF
Hotline AI hat keinen Server und sammelt nichts: kein Konto, keine Analysen, keine Werbung. Deine Nachrichten gehen nur
an den KI-Dienst, den du wählst, zu dessen Bedingungen. API-Schlüssel bleiben in der Windows-Anmeldeinformationsverwaltung;
Verlauf, Einstellungen und Gedächtnis bleiben auf deinem PC. Kostenlos und Open Source.

Keine Copilot-Taste? Lege eine beliebige Tastenkombination fest, z. B. Ctrl+Alt+H, indem du sie in den Einstellungen
drückst.

Hotline AI ist eine unabhängige App und steht in keiner Verbindung zu Microsoft, Anthropic, Google oder OpenAI und wird
von diesen nicht unterstützt. Für Claude Code, Codex, die Antigravity CLI oder eine KI-API brauchst du ein eigenes Konto bei
dem jeweiligen Dienst.

## Neuigkeiten in dieser Version

Ordne Schaltflächenzeilen in den Einstellungen an, füge Schaltflächen mit eigenen Bildern hinzu und nutze ein Mehr-Menü. Lege Schaltflächenbreiten in Zellen fest und passe die Größe jedes Auswahlfelds separat an. Die Schaltflächen der Leiste – auch deine eigenen – können Tastenkombinationen haben, solange das Panel den Fokus hat. Einstellungen > Backup überträgt deine Konfiguration ohne API-Schlüssel; fehlerhafte Einstellungswerte warnen, und deine manuellen Bearbeitungen werden vor dem Ersetzen gesichert. „Weiterführen in“ öffnet den Chat in Open WebUI, llama.cpp, LM Studio oder eigenen Zielen, teils mit manuellem Einfügen. Fensterbreiten nutzen echte Pixel, und der Chat kann breiter werden. Außerdem habe ich die Fokusbehandlung der Copilot-Taste unter Windows 11 26H2, MCP-Tool-Prüfungen, API-Antwortlimits und Backup-Fehler behoben.

## Produktmerkmale

1. Öffnet sich per Copilot-Taste (oder beliebiger Tastenkombination) auf dem Monitor, an dem du arbeitest, bereit zum Tippen
2. Funktioniert mit deiner Claude Code-, ChatGPT- (Codex CLI) oder Gemini-Anmeldung (Antigravity CLI): kein API-Schlüssel nötig
3. Eigener Schlüssel: Anthropic, Google Gemini, OpenAI und OpenAI-kompatible APIs
4. Lokale Modelle: jeder Server im OpenAI-Stil auf deinem PC, sodass der Chat auf deinem Computer bleibt
5. Modell, Anbieter und Denkaufwand direkt im Panel wählen
6. Markierter Text aus der vorherigen App wird automatisch angehängt
7. Fenster, Bildschirm oder Bereich aufnehmen; Bilder und Dateien einfügen oder hineinziehen
8. Text in Screenshots wird auf deinem PC gelesen (OCR) – für Modelle ohne Bildverständnis
9. Schnellaktionen: /translate, /summarize, /fix, /explain und mehr, oder eigene Textdateien
10. Ein Gedächtnis, das jeder Chat sieht, egal welche KI du nutzt
11. Push-to-Talk-Spracheingabe über die Online-Spracherkennung von Windows
12. Text oder Bilder aus Click to Do und anderen Apps senden; Chats im Terminal, Open WebUI, llama.cpp oder LM Studio fortsetzen
13. Reichhaltige Antworten: Tabellen, Code mit Kopieren, Formeln und Zeichnungen
14. Natives WinUI 3 auf x64 und ARM64; helles, dunkles und Kontrast-Design, deine Akzentfarbe, kein eingebetteter Browser
15. Schaltflächenzeilen per Drag-and-Drop in den Einstellungen anordnen; eigene Buttons mit Bildern, Mehr-Menü und Breitenanpassung
16. Optionale MCP-Tools für API-Verbindungen; Änderungen fragen zuerst nach, sofern nicht vorab erlaubt
17. Kein Hotline-Konto, keine Analysen, keine Werbung; Schlüssel bleiben in der Windows-Anmeldeinformationsverwaltung; kostenlos und Open Source
18. Bearbeitbare Einstellungen, Verbindungen, Prompts und toolbar.json; Warnung bei Fehlwerten, Backups schützen deine Anpassungen
19. Einstellungen > Backup exportiert und importiert Einstellungen, Leiste, MCP, Töne, Verbindungen, Schnellaktionen und Prompts; nie API-Schlüssel
20. Tastenkombinationen im Panel für Leisten-Buttons und eigene Aktionen; Panel verschieben, anheften, skalieren, mit Pixelbreiten und breiterem Chat

## Screenshot-Beschriftungen

1. Copilot-Taste drücken: Frag irgendwas, die Antwort erscheint live.
2. Markierter Text kommt mit deiner Frage mit.
3. Schnellaktionen mit /: übersetzen, zusammenfassen, korrigieren, erklären.
4. Nutze die KI, die du schon hast, und richte alles nach deinem Geschmack ein.

## Suchbegriffe

KI-Chat, Copilot-Taste, Claude, Gemini, KI-Assistent, LLM, lokale KI
