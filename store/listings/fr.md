# Fiche Store — Hotline AI (Français)

Model-drafted translation of `store/listing.md`; have a native speaker review it before publishing. Paste into Partner
Center › the submission › Store listings › Français. Product name stays "Hotline AI".

## Description courte

Utilisez la touche Copilot pour un panneau de discussion IA natif. Utilisez votre connexion Claude, Codex ou Gemini, votre propre clé API ou un modèle local. Organisez la barre, ajoutez vos boutons et poursuivez l'échange dans une autre application pour plus d'espace.

## Description

J'ai créé cette application parce que la touche Copilot ne me servait à rien. Hotline AI en fait un panneau de discussion IA natif qui parle à l'IA de votre choix.

Appuyez sur la touche et un panneau s'ouvre sur l'écran où vous travaillez, prêt à écrire. Posez une question, la réponse s'affiche au fil de l'eau ; appuyez de nouveau sur la touche et il disparaît. Pas d'onglet de navigateur, pas de compte chez nous.

UTILISEZ L'IA QUE VOUS AVEZ DÉJÀ
• Claude Code : utilise votre Claude Code installé et sa connexion ; votre forfait Claude fonctionne sans clé API.
• CLI OpenAI Codex : utilise votre Codex installé et sa connexion ; votre forfait ChatGPT fonctionne sans clé API.
• CLI Antigravity (agy) : Gemini avec votre compte Google.
• Votre propre clé API : Anthropic, Google Gemini, OpenAI et d'autres services compatibles OpenAI comme OpenRouter.
• Modèles locaux : n'importe quel serveur de type OpenAI sur votre PC (par exemple llama.cpp ou LM Studio), pour que la discussion reste sur votre ordinateur.
Choisissez le modèle, le fournisseur et l'effort de réflexion directement depuis la barre du panneau.
Vous utilisez votre propre service ou exécutez le modèle vous-même. Hotline n'inclut aucun abonnement IA.

EMPORTEZ LE CONTEXTE
• Le texte sélectionné dans une autre application est joint automatiquement quand vous appuyez sur la touche, et vous pouvez le retirer avant l'envoi.
• Capturez la fenêtre où vous étiez, tout l'écran ou une zone que vous tracez ; collez ou déposez images et fichiers.
• Les modèles qui ne voient pas les images reçoivent à la place le texte de votre capture, lu sur votre PC avec l'OCR de Windows.

FAIT POUR LES TÂCHES RAPIDES
• Actions rapides : tapez / pour traduire, résumer, corriger, expliquer, répondre, raccourcir, formaliser ou créer des listes à puces, ou écrivez les vôtres dans de simples fichiers texte.
• Mémoire : « /remember je préfère les unités métriques » et chaque conversation le sait, quelle que soit l'IA utilisée.
• Voix : attribuez l'appui long à la saisie vocale, maintenez la touche et parlez ; relâchez pour arrêter. Cela utilise la reconnaissance vocale en ligne de Windows.
• Les conversations récentes sont à un clic, dans une liste avec recherche. Ctrl+H ou /resume l'ouvre également. Les discussions CLI restent dans l'historique propre du CLI et peuvent être reprises après un redémarrage.
• Click to Do et actions Windows : envoyez du texte ou une image à Hotline AI depuis d'autres applications.
• Continuer dans : poursuivez la discussion dans un terminal, Open WebUI, l'interface web de llama.cpp, LM Studio ou la cible web ou applicative de votre choix. Open WebUI reçoit une véritable discussion. LM Studio s'ouvre avec la discussion copiée, prête à être collée ; le fonctionnement varie selon les autres cibles.
• Lorsque le panneau a le focus, utilisez des raccourcis pour une nouvelle discussion, les conversations récentes, Paramètres, épingler, copier la dernière réponse, les captures, les prompts et les sélecteurs de modèle, de fournisseur et d'effort. Vos propres boutons peuvent aussi en avoir un. Ils nécessitent Ctrl, Alt ou Win, pour ne jamais interférer avec la saisie. Modifiez-les dans Paramètres ou dans toolbar.json.
• Recevez une notification Windows lorsqu'une réponse se termine pendant que vous êtes sur une autre fenêtre.

NATIF, LÉGER ET À VOUS
• Conçu avec WinUI 3 : s'ouvre vite, suit votre thème clair ou sombre, sans navigateur intégré. Natif sur x64 et ARM64.
• Choisissez une couleur d'accentuation ou utilisez celle de Windows. Un thème Contraste élevé est proposé, et Hotline suit les thèmes de contraste et les réglages de transparence de Windows.
• Réponses riches : titres, listes, tableaux, code avec bouton Copier et défilement horizontal, formules et dessins simples. Le panneau suit les réponses à mesure qu'elles arrivent.
• Maintenez Alt pour déplacer ou redimensionner le panneau. Épinglez-le, choisissez si la discussion s'étend vers le haut ou vers le bas, et définissez des sons d'ouverture et de fermeture. La largeur des fenêtres utilise de vrais pixels. Vous pouvez avoir un panneau étroit à l'ouverture qui s'élargit lorsqu'une discussion commence.
• Tout est un simple fichier modifiable : paramètres, connexions, prompts, boutons de la barre.
• Rangées de boutons v2 : faites glisser les boutons d'une rangée à l'autre dans Paramètres > Fenêtre > Rangées de boutons, ou modifiez toolbar.json. Ajoutez des boutons pour des actions rapides ou du texte prédéfini, avec votre propre image PNG. Définissez les largeurs en cellules, ajustez la taille de chaque sélecteur séparément et placez les boutons moins utilisés dans un menu Plus. Une barre étroite déplace les boutons qui ne rentrent pas dans un menu.
• Une valeur de paramètre incorrecte bascule sur une valeur de repli avec un avertissement ; les autres paramètres s'appliquent toujours. Avant de remplacer un fichier de paramètres, de connexion ou de barre d'outils modifié à la main, Hotline en conserve une sauvegarde. Si cette sauvegarde échoue, le fichier reste intact. L'application ne crée pas de nouvelle sauvegarde lorsqu'elle remplace son propre dernier enregistrement.
• Paramètres > Sauvegarde exporte et importe les paramètres, les rangées de boutons et leurs images, les outils MCP, les sons, les connexions, les actions rapides et les prompts. L'importation sauvegarde d'abord chaque fichier remplacé ; pas de sauvegarde, pas d'écriture. Les clés API ne sont jamais exportées : saisissez-les à nouveau sur l'autre PC. L'historique et la mémoire ne font pas partie du paquet ; les variables d'environnement MCP y figurent, gardez donc le fichier privé si vous y avez placé des secrets.
• Des outils, avec prudence : les connexions API peuvent utiliser les outils de vos serveurs MCP ; tout ce qui modifie quelque chose vous est d'abord demandé, sauf si vous l'avez autorisé. Les outils en lecture seule s'exécutent sans confirmation, selon ce que déclare le serveur.

PRIVÉ PAR CONCEPTION
Hotline AI n'a pas de serveur et ne collecte rien : pas de compte, pas de statistiques, pas de publicité. Vos messages vont uniquement au service d'IA que vous choisissez, selon ses conditions. Les clés API restent dans le Gestionnaire d'informations d'identification de Windows ; l'historique, les paramètres et la mémoire restent sur votre PC. Gratuit et open source.

Pas de touche Copilot ? Définissez le raccourci de votre choix, par exemple Ctrl+Alt+H, en appuyant simplement dessus dans Paramètres.

Hotline AI est une application indépendante, non affiliée à Microsoft, Anthropic, Google ou OpenAI, ni approuvée par eux. L'utilisation de Claude Code, de Codex, de la CLI Antigravity ou d'une API d'IA nécessite votre propre compte auprès de ce service.

## Nouveautés de cette version

Organisez les rangées de boutons dans Paramètres, ajoutez des boutons avec vos propres images et utilisez un menu Plus. Définissez la largeur des boutons en cellules et ajustez la taille de chaque sélecteur séparément. Les boutons de la barre, y compris les vôtres, peuvent avoir des raccourcis clavier lorsque le panneau a le focus. Paramètres > Sauvegarde transfère votre configuration sans les clés API ; les valeurs de paramètres incorrectes affichent un avertissement, et vos modifications sont sauvegardées avant d'être remplacées. Continuer dans ouvre la discussion dans Open WebUI, llama.cpp, LM Studio ou vos propres cibles, parfois avec un collage à effectuer. La largeur des fenêtres utilise de vrais pixels, et la discussion peut s'élargir. J'ai aussi corrigé la gestion du focus de la touche Copilot sur Windows 11 26H2, les vérifications d'outils MCP, les limites de réponse d'API et les échecs de sauvegarde.

## Fonctionnalités du produit

1. S'ouvre avec la touche Copilot (ou n'importe quel raccourci) sur l'écran où vous travaillez, prêt à écrire
2. Fonctionne avec votre connexion Claude Code, ChatGPT (CLI Codex) ou Gemini (CLI Antigravity) : aucune clé API nécessaire
3. Votre propre clé : Anthropic, Google Gemini, OpenAI et API compatibles OpenAI
4. Modèles locaux : n'importe quel serveur de type OpenAI sur votre PC, pour que la discussion reste sur votre ordinateur
5. Choisissez le modèle, le fournisseur et l'effort de réflexion depuis le panneau
6. Le texte sélectionné dans l'application d'origine est joint automatiquement
7. Capturez une fenêtre, l'écran ou une zone ; collez ou déposez images et fichiers
8. Le texte des captures est lu sur votre PC (OCR) pour les modèles qui ne voient pas les images
9. Actions rapides : /translate, /summarize, /fix, /explain et d'autres, ou vos propres fichiers texte
10. Une mémoire visible par chaque discussion, quelle que soit l'IA utilisée
11. Saisie vocale en maintenant la touche grâce à la reconnaissance vocale en ligne de Windows
12. Envoyez du texte ou des images depuis Click to Do ; continuez dans un terminal, Open WebUI, llama.cpp ou LM Studio
13. Réponses riches : tableaux, code avec bouton Copier, formules et dessins
14. WinUI 3 natif sur x64 et ARM64 ; thèmes clair, sombre et contraste, votre couleur d'accentuation, sans navigateur
15. Réorganisez les rangées dans Paramètres ; boutons personnalisés avec vos images, menu Plus et sélecteurs ajustables
16. Outils MCP optionnels pour l'API ; les modifications requièrent votre accord sauf autorisation préalable
17. Ni compte, ni statistiques, ni pub ; clés dans le Gestionnaire d'identifiants Windows ; gratuit et open source
18. Paramètres, connexions, prompts et toolbar.json modifiables ; avertissement en cas d'erreur et sauvegardes automatiques
19. Paramètres > Sauvegarde exporte et importe réglages, barre, MCP, sons, connexions et prompts ; jamais les clés API
20. Raccourcis pour les boutons de la barre ; déplacez, épinglez et redimensionnez en pixels avec discussion élargie

## Légendes des captures d'écran

1. Appuyez sur la touche Copilot : demandez ce que vous voulez, la réponse arrive au fil de l'eau.
2. Le texte sélectionné accompagne votre question.
3. Actions rapides avec / : traduire, résumer, corriger, expliquer.
4. Utilisez l'IA que vous avez déjà et réglez tout à votre façon.

## Termes de recherche

chat IA, touche Copilot, Claude, Gemini, assistant IA, LLM, IA locale
