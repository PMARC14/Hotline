# Scheda dello Store — Hotline AI (Italiano)

Model-drafted translation of `store/listing.md`; have a native speaker review it before publishing. Paste into Partner
Center › the submission › Store listings › Italiano. Product name stays "Hotline AI".

## Descrizione breve

Usa il tasto Copilot per un pannello di chat IA nativo. Porta il tuo accesso a Claude, Codex o Gemini, la tua chiave API o un modello locale. Disponi la barra, aggiungi i tuoi pulsanti e sposta la chat in un'altra app quando ti serve più spazio.

## Descrizione

L'ho creata perché per me il tasto Copilot non fa nulla di utile. Hotline AI lo trasforma in un pannello di chat IA nativo che parla con l'IA che scegli tu.

Premi il tasto e si apre un pannello sul monitor su cui stai lavorando, pronto per scrivere. Fai una domanda, ricevi la risposta in tempo reale, premi di nuovo il tasto e sparisce. Nessuna scheda del browser, nessun account presso di noi.

USA L'IA CHE HAI GIÀ
• Claude Code: usa il tuo Claude Code installato e il suo accesso, quindi il tuo piano Claude funziona senza chiave API.
• CLI OpenAI Codex: usa il tuo Codex installato e il suo accesso, quindi il tuo piano ChatGPT funziona senza chiave API.
• CLI di Antigravity (agy): Gemini con il tuo account Google.
• La tua chiave API: Anthropic, Google Gemini, OpenAI e altri servizi compatibili con OpenAI come OpenRouter.
• Modelli locali: qualsiasi server in stile OpenAI sul tuo PC (ad esempio llama.cpp o LM Studio), così la chat resta sul tuo computer.
Scegli modello, provider e quanto deve ragionare direttamente dalla barra del pannello.
Il servizio lo porti tu oppure esegui il modello in autonomia. Hotline non include un abbonamento IA.

PORTA CON TE IL CONTESTO
• Il testo selezionato in un'altra app viene allegato automaticamente quando premi il tasto, e puoi rimuoverlo prima dell'invio.
• Cattura la finestra in cui eri, l'intero schermo o un'area che trascini; incolla o trascina immagini e file.
• I modelli che non vedono le immagini ricevono invece il testo dello screenshot, letto sul tuo PC con l'OCR di Windows.

FATTO PER I LAVORI VELOCI
• Azioni rapide: digita / per tradurre, riassumere, correggere, spiegare, rispondere, accorciare, rendere formale o creare elenchi puntati (/translate, /summarize, /fix, /explain, /reply, /shorter, /formal o /bullets), oppure crea le tue come semplici file di testo.
• Memoria: «/remember preferisco le unità metriche» e ogni chat lo sa, qualunque IA tu usi.
• Voce: imposta la pressione prolungata su Input vocale, tieni premuto il tasto e parla; rilascialo per fermarti. Usa il riconoscimento vocale online di Windows.
• Le chat recenti sono a portata di clic, in un elenco con ricerca. Anche Ctrl+H o /resume lo aprono. Le chat CLI restano nella cronologia della CLI e possono essere riprese dopo un riavvio.
• Click to Do e azioni di Windows: invia testo o un'immagine a Hotline AI da altre app.
• Continua in: porta la chat in un terminale, Open WebUI, l'interfaccia web di llama.cpp, LM Studio o una tua destinazione web o app. Open WebUI riceve una chat vera e propria. LM Studio si apre con la chat copiata pronta da incollare; per le altre destinazioni il funzionamento varia.
• Mentre il pannello ha lo stato attivo, usa le scorciatoie per nuova chat, chat recenti, Impostazioni, blocca in primo piano, copia l'ultima risposta, acquisizioni, prompt e i selettori di modello, provider e impegno di ragionamento. Anche i tuoi pulsanti possono averne una. Richiedono Ctrl, Alt o Win, così la digitazione non viene mai intaccata. Modificali in Impostazioni o in toolbar.json.
• Ricevi una notifica di Windows quando una risposta finisce mentre sei altrove.

NATIVO, LEGGERO E TUO
• Realizzato con WinUI 3: si apre in fretta, segue il tuo tema chiaro o scuro, senza browser integrato. Nativo su x64 e ARM64.
• Scegli un colore principale o usa quello di Windows. C'è un tema Contrasto elevato, e Hotline segue i temi a contrasto e l'impostazione di trasparenza di Windows.
• Risposte complete: titoli, elenchi, tabelle, codice con pulsante Copia e scorrimento orizzontale, formule e disegni semplici. Il pannello segue le risposte mentre arrivano in tempo reale.
• Tieni premuto Alt per spostare o ridimensionare il pannello. Bloccalo in primo piano, scegli se la chat cresce verso l'alto o verso il basso e imposta i suoni di apertura e chiusura. La larghezza della finestra usa pixel reali. Puoi avere un pannello che si apre stretto e si allarga quando si avvia una chat.
• Tutto è un semplice file modificabile: impostazioni, connessioni, prompt, i pulsanti della barra.
• Righe di pulsanti v2: trascina i pulsanti da una riga all'altra in Impostazioni > Finestra > Righe di pulsanti, oppure modifica toolbar.json. Aggiungi pulsanti per azioni rapide o testo predefinito, con la tua immagine PNG personalizzata. Scegli le larghezze in celle, dimensiona ogni selettore separatamente e sposta i pulsanti meno usati in un menu Altro. Una barra stretta sposta i pulsanti in eccesso in un menu.
• Un valore non valido nelle impostazioni usa un fallback con un avviso; le altre impostazioni continuano ad applicarsi. Prima di sostituire un file di impostazioni, connessioni o toolbar modificato a mano, Hotline conserva un backup. Se il backup fallisce, lascia intatto il file. Non crea un altro backup quando sostituisce il proprio ultimo salvataggio.
• Impostazioni > Backup esporta e importa impostazioni, righe di pulsanti e immagini, strumenti MCP, suoni, connessioni, azioni rapide e prompt. L'importazione esegue prima il backup di ogni file che sostituisce; nessun backup significa nessuna scrittura. Le chiavi API non vengono mai esportate: inseriscile di nuovo sull'altro PC. Cronologia e memoria non sono incluse nel pacchetto; i valori delle variabili di ambiente MCP invece sì, quindi mantieni privato il pacchetto se contiene segreti.
• Strumenti, con attenzione: le connessioni API possono usare gli strumenti dei tuoi server MCP; tutto ciò che modifica qualcosa ti chiede prima conferma a meno che tu non lo abbia consentito. Gli strumenti di sola lettura vengono eseguiti senza chiedere conferma, in base a quanto dichiarato dal server.

PRIVATO PER SCELTA
Hotline AI non ha server e non raccoglie nulla: nessun account, nessuna analisi, nessuna pubblicità. I tuoi messaggi vanno solo al servizio di IA che scegli, secondo le sue condizioni. Le chiavi API restano in Gestione credenziali di Windows; cronologia, impostazioni e memoria restano sul tuo PC. Gratuito e open source.

Niente tasto Copilot? Imposta la scorciatoia che preferisci, ad esempio Ctrl+Alt+H, semplicemente premendola in Impostazioni.

Hotline AI è un'app indipendente e non è affiliata né approvata da Microsoft, Anthropic, Google o OpenAI. Per usare Claude Code, Codex, la CLI di Antigravity o un'API di IA serve un tuo account presso quel servizio.

## Novità di questa versione

Disponi le righe di pulsanti in Impostazioni, aggiungi pulsanti con le tue immagini e usa un menu Altro. Imposta le larghezze dei pulsanti in celle e dimensiona ogni selettore separatamente. I pulsanti della barra, inclusi i tuoi, possono avere scorciatoie da tastiera mentre il pannello ha lo stato attivo. Impostazioni > Backup sposta la tua configurazione senza chiavi API; i valori non validi delle impostazioni mostrano un avviso e le modifiche manuali vengono salvate in un backup prima della sostituzione. Continua in apre la chat in Open WebUI, llama.cpp, LM Studio o nelle tue destinazioni personalizzate, a volte con la necessità di incollare il testo. La larghezza della finestra usa pixel reali e la chat può allargarsi. Ho anche corretto la gestione dello stato attivo del tasto Copilot su Windows 11 26H2, i controlli degli strumenti MCP, i limiti di risposta delle API e gli errori di backup.

## Caratteristiche del prodotto

1. Si apre con il tasto Copilot (o qualsiasi scorciatoia) sul monitor su cui lavori, pronto per scrivere
2. Funziona con il tuo accesso a Claude Code, ChatGPT (CLI di Codex) o Gemini (CLI di Antigravity): nessuna chiave API
3. La tua chiave: Anthropic, Google Gemini, OpenAI e API compatibili con OpenAI
4. Modelli locali: qualsiasi server in stile OpenAI sul tuo PC, così la chat resta sul tuo computer
5. Scegli modello, provider e impegno di ragionamento dal pannello
6. Il testo selezionato nell'app di provenienza viene allegato automaticamente
7. Cattura una finestra, lo schermo o un'area; incolla o trascina immagini e file
8. Il testo degli screenshot viene letto sul tuo PC (OCR) per i modelli che non vedono le immagini
9. Azioni rapide: /translate, /summarize, /fix, /explain e altre, o file di testo personalizzati
10. Una memoria che ogni chat vede, qualunque IA tu usi
11. Input vocale push-to-talk con il riconoscimento vocale online di Windows
12. Invia testo o immagini da Click to Do e altre app; continua le chat in un terminale, Open WebUI, llama.cpp o LM Studio
13. Risposte complete: tabelle, codice con pulsante Copia, formule e disegni
14. WinUI 3 nativo su x64 e ARM64; temi chiaro, scuro e a contrasto, colore personalizzato, nessun browser integrato
15. Trascina le righe di pulsanti in Impostazioni; pulsanti personalizzati con immagini proprie, menu Altro e larghezze separate
16. Strumenti MCP opzionali per connessioni API; le modifiche chiedono prima conferma se non autorizzate
17. Nessun account Hotline, analisi o pubblicità; le chiavi restano in Gestione credenziali di Windows; gratuito e open source
18. Impostazioni, connessioni, prompt e toolbar.json modificabili; valori non validi segnalati, i backup proteggono le modifiche
19. Impostazioni > Backup esporta e importa impostazioni, barra, MCP, suoni, connessioni, azioni rapide e prompt; mai chiavi API
20. Scorciatoie nel pannello per i pulsanti della barra, inclusi i tuoi; sposta, blocca e ridimensiona in pixel, con chat allargabile

## Didascalie degli screenshot

1. Premi il tasto Copilot: chiedi qualsiasi cosa, la risposta arriva in tempo reale.
2. Il testo selezionato accompagna la tua domanda.
3. Azioni rapide con /: tradurre, riassumere, correggere, spiegare.
4. Usa l'IA che hai già e imposta tutto a modo tuo.

## Termini di ricerca

chat IA, tasto Copilot, Claude, Gemini, assistente IA, LLM, IA locale
