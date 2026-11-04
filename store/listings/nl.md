# Store-vermelding — Hotline AI (Nederlands)

Model-drafted translation of `store/listing.md`; have a native speaker review it before publishing. Paste into Partner
Center › the submission › Store listings › Nederlands. Product name stays "Hotline AI".

## Korte beschrijving

Gebruik de Copilot-toets voor een native AI-chatpaneel. Gebruik je Claude-, Codex- of Gemini-aanmelding, je eigen API-sleutel of een lokaal model. Deel de balk in, voeg eigen knoppen toe en zet de chat voort in een andere app als je meer ruimte nodig hebt.

## Beschrijving

Ik heb dit gemaakt omdat de Copilot-toets voor mij niets nuttigs doet. Hotline AI maakt er een native AI-chatpaneel van dat praat met de AI die jij kiest.

Druk op de toets en er opent een paneel op het scherm waarop je werkt, klaar om te typen. Stel een vraag, zie het antwoord live verschijnen, druk nogmaals op de toets en het is weg. Geen browsertabblad, geen account bij ons.

GEBRUIK DE AI DIE JE AL HEBT
• Claude Code: gebruikt je geïnstalleerde Claude Code en de aanmelding daarvan, dus je Claude-abonnement werkt zonder API-sleutel.
• OpenAI Codex CLI: gebruikt je geïnstalleerde Codex en de aanmelding daarvan, dus je ChatGPT-abonnement werkt zonder API-sleutel.
• Antigravity CLI (agy): Gemini met je Google-aanmelding.
• Je eigen API-sleutel: Anthropic, Google Gemini, OpenAI en andere OpenAI-compatibele diensten zoals OpenRouter.
• Lokale modellen: elke server in OpenAI-stijl op je pc (bijvoorbeeld llama.cpp of LM Studio), zodat de chat op je computer blijft.
Kies het model, de provider en hoeveel er nagedacht moet worden direct vanuit de balk van het paneel.
Je zorgt zelf voor de dienst of draait het model zelf. Hotline bevat geen AI-abonnement.

NEEM JE CONTEXT MEE
• Tekst die je in een andere app had geselecteerd, wordt automatisch bijgevoegd als je op de toets drukt; je kunt hem vóór het verzenden weghalen.
• Leg het venster vast waarin je zat, het hele scherm of een gebied dat je uitsleept; plak of sleep afbeeldingen en bestanden.
• Modellen die geen afbeeldingen kunnen zien, krijgen in plaats daarvan de tekst uit je schermafbeelding, op je pc gelezen met de OCR van Windows.

GEMAAKT VOOR SNELLE KLUSSEN
• Snelle acties: typ / voor translate, summarize, fix, explain, reply, shorter, formal of bullets, of schrijf je eigen acties als gewone tekstbestanden.
• Geheugen: "/remember ik gebruik liever metrische eenheden" en elke chat weet het, welke AI je ook gebruikt.
• Spraak: stel lang indrukken in op Spraakinvoer, houd de toets ingedrukt en praat; laat los om te stoppen. Dit gebruikt de online spraakherkenning van Windows.
• Recente chats zijn één klik verwijderd, in een doorzoekbare lijst. Ctrl+H of /resume opent deze ook. CLI-chats blijven in de eigen geschiedenis van de CLI en kunnen na een herstart worden hervat.
• Click to Do en Windows-acties: stuur tekst of een afbeelding vanuit andere apps naar Hotline AI.
• Doorgaan in: zet de chat voort in een terminal, Open WebUI, de web-UI van llama.cpp, LM Studio of je eigen web- of app-doel. Open WebUI krijgt een echte chat. LM Studio opent met de chat gekopieerd zodat je hem kunt plakken; bij andere doelen verschilt dit.
• Terwijl het paneel de focus heeft, gebruik je sneltoetsen voor een nieuwe chat, recente chats, Instellingen, vastpinnen, het laatste antwoord kopiëren, opnames, prompts en de kiezers voor model, provider en denkinspanning. Je eigen knoppen kunnen er ook een hebben. Ze vereisen Ctrl, Alt of Win, zodat het typen nooit wordt verstoord. Wijzig ze in Instellingen of in toolbar.json.
• Ontvang een Windows-melding wanneer een antwoord klaar is terwijl je ergens anders mee bezig bent.

NATIVE, LICHT EN VAN JOU
• Gebouwd met WinUI 3: opent snel, volgt je lichte of donkere thema, zonder ingebouwde browser. Native op x64 en ARM64.
• Kies een accentkleur of gebruik die van Windows. Er is een thema Hoog contrast, en Hotline volgt de contrastthema's en transparantie-instelling van Windows.
• Rijke antwoorden: koppen, lijsten, tabellen, code met een knop Kopiëren en zijwaarts scrollen, formules en eenvoudige tekeningen. Het paneel volgt antwoorden terwijl ze binnenkomen.
• Houd Alt ingedrukt om het paneel te verplaatsen of van grootte te veranderen. Pin het vast, kies of de chat naar boven of beneden groeit, en stel geluiden in voor openen en sluiten. Vensterbreedtes gebruiken echte pixels. Je kunt een smal beginpaneel instellen dat breder wordt zodra een chat start.
• Alles is een gewoon bestand dat je kunt bewerken: instellingen, verbindingen, prompts en de knoppen in de balk.
• Knoppenrijen v2: sleep knoppen tussen rijen in Instellingen > Venster > Knoppenrijen, of bewerk toolbar.json. Voeg knoppen toe voor snelle acties of vooraf ingestelde tekst, met je eigen PNG-afbeelding. Kies breedtes in cellen, stel de grootte van elke kiezer apart in en plaats minder gebruikte knoppen in een menu Meer. Een smalle balk verplaatst knoppen die niet passen naar een menu.
• Een ongeldige instellingswaarde valt terug met een waarschuwing; de overige instellingen blijven gewoon gelden. Voordat een handmatig bewerkt instellingen-, verbindings- of werkbalkbestand wordt overschreven, maakt Hotline een back-up. Als die back-up mislukt, blijft het bestand onaangeroerd. Het maakt geen nieuwe back-up bij het vervangen van zijn eigen laatste opslag.
• Instellingen > Back-up exporteert en importeert instellingen, knoppenrijen en afbeeldingen, MCP-hulpmiddelen, geluiden, verbindingen, snelle acties en prompts. Importeren maakt eerst een back-up van elk bestand dat het vervangt; geen back-up betekent niet schrijven. API-sleutels worden nooit geëxporteerd; voer ze op de andere pc opnieuw in. Geschiedenis en geheugen zitten niet in het pakket; MCP-omgevingsvariabelen wel, dus houd het pakket privé als je daar geheimen in hebt gezet.
• Hulpmiddelen, met beleid: API-verbindingen kunnen hulpmiddelen van je MCP-servers gebruiken; alles wat iets wijzigt, vraagt het je eerst, tenzij je het hebt toegestaan. Alleen-lezen hulpmiddelen draaien zonder te vragen, op basis van wat de server opgeeft.

PRIVACY VOOROP
Hotline AI heeft geen server en verzamelt niets: geen account, geen analyses, geen advertenties. Je berichten gaan alleen naar de AI-dienst die jij kiest, onder de voorwaarden van die dienst. API-sleutels blijven in Windows Referentiebeheer; geschiedenis, instellingen en geheugen blijven op je pc. Gratis en open source.

Geen Copilot-toets? Stel een sneltoets naar keuze in, bijvoorbeeld Ctrl+Alt+H, door deze in Instellingen in te drukken.

Hotline AI is een onafhankelijke app en is niet verbonden aan of goedgekeurd door Microsoft, Anthropic, Google of OpenAI. Voor het gebruik van Claude Code, Codex, de Antigravity CLI of een AI-API heb je een eigen account bij die dienst nodig.

## Nieuw in deze versie

Deel knoppenrijen in via Instellingen, voeg knoppen toe met je eigen afbeeldingen en gebruik een menu Meer. Stel knopbreedtes in cellen in en pas het formaat van elke kiezer afzonderlijk aan. De knoppen in de balk, inclusief je eigen knoppen, kunnen sneltoetsen hebben terwijl het paneel de focus heeft. Instellingen > Back-up verplaatst je configuratie zonder API-sleutels; bij ongeldige instellingswaarden krijg je een waarschuwing en vóór vervanging wordt er een back-up van je bewerkingen gemaakt. 'Doorgaan in' opent de chat in Open WebUI, llama.cpp, LM Studio of je eigen doelen, waarbij soms plakken nodig is. Vensterbreedtes gebruiken echte pixels en de chat kan breder worden. Ook heb ik de focusafhandeling van de Copilot-toets op Windows 11 26H2, controles van MCP-hulpmiddelen, limieten voor API-antwoorden en back-upfouten opgelost.

## Productkenmerken

1. Opent met de Copilot-toets (of een eigen sneltoets) op het scherm waarop je werkt, klaar om te typen
2. Werkt met je aanmelding voor Claude Code, ChatGPT (Codex CLI) of Gemini (Antigravity CLI): geen API-sleutel nodig
3. Eigen sleutel: Anthropic, Google Gemini, OpenAI en OpenAI-compatibele API's
4. Lokale modellen: elke server in OpenAI-stijl op je pc, zodat de chat op je computer blijft
5. Kies het model, de provider en de denkinspanning direct in het paneel
6. Geselecteerde tekst uit de app waar je vandaan kwam, wordt automatisch bijgevoegd
7. Leg een venster, het scherm of een gebied vast; plak of sleep afbeeldingen en bestanden
8. Tekst in schermafbeeldingen wordt op je pc gelezen (OCR) voor modellen die geen afbeeldingen zien
9. Snelle acties: /translate, /summarize, /fix, /explain en meer, of je eigen tekstbestanden
10. Een geheugen dat elke chat ziet, welke AI je ook gebruikt
11. Spraakinvoer via push-to-talk met behulp van de online spraakherkenning van Windows
12. Stuur tekst of afbeeldingen vanuit Click to Do en andere apps; zet chats voort in een terminal, Open WebUI, llama.cpp of LM Studio
13. Rijke antwoorden: tabellen, code met knop Kopiëren, formules en tekeningen
14. Native WinUI 3 op x64 en ARM64; lichte, donkere en contrastthema's, je eigen accentkleur, geen ingebouwde browser
15. Sleep knoppenrijen in Instellingen; eigen knoppen met eigen afbeeldingen, een menu Meer en aparte breedtes voor kiezers
16. Optionele MCP-hulpmiddelen voor API-verbindingen; wijzigingen vragen eerst bevestiging tenzij vooraf toegestaan
17. Geen Hotline-account, analyses of advertenties; sleutels blijven in Windows Referentiebeheer; gratis en open source
18. Bewerkbare instellingen, verbindingen, prompts en toolbar.json; ongeldige waarden waarschuwen en vallen terug, back-ups beschermen bewerkingen
19. Instellingen > Back-up exporteert en importeert instellingen, balk, MCP, geluiden, verbindingen, acties en prompts; nooit API-sleutels
20. Sneltoetsen in het paneel voor balkknoppen en eigen knoppen; verplaats, pin en schaal het paneel met pixelbreedtes en een bredere chatoptie

## Bijschriften bij schermafbeeldingen

1. Druk op de Copilot-toets: vraag wat je wilt, antwoorden verschijnen live.
2. Geselecteerde tekst gaat mee met je vraag.
3. Snelle acties met /: vertalen, samenvatten, verbeteren, uitleggen.
4. Gebruik de AI die je al hebt en stel alles in zoals jij wilt.

## Zoektermen

AI-chat, Copilot-toets, Claude, Gemini, AI-assistent, LLM, lokale AI
