# Cahier des charges : jumeau numérique d'une ligne de production

Mini-projet Industrie 4.0, ENSAM Rabat, 2026/2027. Encadrante : Pr. Maria Zemzami.
Équipe : Aymane ISSAMI, Marouan EZBAKHE, Hiba BENZAHRA, Zaid HSAIN, Houssam KICHCHOU, Mohamed HAMDOUCH.

Ce document suit les rubriques demandées dans le sujet (description du contexte et des objectifs, spécifications fonctionnelles et techniques, aspects humains, livrables, planning et ressources, critères d'évaluation).

## I. Description du contexte et des objectifs

### Contexte

L'Industrie 4.0 relie les équipements, les données et les décisions : une usine produit, mesure et s'ajuste en continu. Le jumeau numérique en est une brique : une copie virtuelle de la ligne, alimentée par des données, qui se comporte comme la ligne réelle. Elle permet de voir, mesurer et tester des changements sans toucher à l'installation.

Une ligne de production a presque toujours un poste qui limite toute la ligne (le goulot). Les pannes et les rejets aggravent les pertes. Tester un changement sur la vraie ligne est coûteux et risqué, et le goulot n'est pas toujours visible.

### Périmètre

**Inclus**
- une ligne simplifiée de 4 postes en série : Alimentation (5 s), Traitement (8 s), Contrôle qualité (15 s), Emballage (7 s) ;
- un convoyeur de 2 s entre les postes, des files de 8 places avant chaque poste, avec blocage si l'aval est plein ;
- des pannes aléatoires (temps moyen entre pannes de 400 à 600 s, réparation de 20 à 30 s) et 5 % de rejets au contrôle ;
- la simulation de cette ligne, sa visualisation 3D dans Unity (navigateur, WebGL) et un tableau de bord de supervision ;
- quatre scénarios d'optimisation, leur comparaison et leur documentation.

**Exclu**
- une usine réelle, de vrais capteurs ou automates (pas de connexion OPC UA ou MQTT dans cette version) ;
- plusieurs lignes ou plusieurs produits ;
- la maintenance prédictive par apprentissage automatique ;
- l'authentification des utilisateurs.

### Objectifs spécifiques

1. Simuler fidèlement la ligne (files, blocages, pannes, rejets) avec un modèle validé par des tests automatisés.
2. Visualiser la ligne en 3D, en temps réel, de façon fluide, dans un navigateur.
3. Superviser la production avec 6 indicateurs : cadence réelle et théorique, produits finis, rejets, en-cours, goulot.
4. Identifier le goulot et tester au moins 3 scénarios d'optimisation, avec comparaison avant et après.
5. Documenter les résultats mesurés avec la scène 3D (voir `docs/resultats-unity.md`).

## II. Spécifications fonctionnelles

| Fonction | Description | Réalisation |
|---|---|---|
| Simulation de la ligne | 4 postes, files, blocages, convoyeur | `backend/sim.py` |
| Pannes et rejets | pannes aléatoires par poste, 5 % de rejets au contrôle | `backend/sim.py` |
| Collecte et analyse en temps réel | état diffusé 10 fois par seconde, KPI calculés par le serveur | `backend/server.py` |
| Interconnexion des composants | un seul flux WebSocket lu par la 3D et le tableau de bord | `docs/protocole-websocket.md` |
| Interface homme-machine | vue 3D (tourner, zoomer, cliquer sur une machine) et tableau de bord | `unity/`, `frontend/` |
| Automatisation et pilotage | scénarios, temps de cycle, machines en parallèle, pannes provoquées, vitesse, pause | commandes WebSocket |
| Historique | un relevé des KPI par minute simulée, événements, export CSV | `backend/storage.py` |
| Maintenance prédictive | non réalisée ; les MTBF et MTTR observés sont affichés comme base | tableau de bord |

## III. Spécifications techniques

### Technologies

| Rôle | Technologie |
|---|---|
| Moteur de simulation | Python (à pas de temps fixe de 0,05 s simulée) |
| Serveur et communication | FastAPI, WebSocket, SQLite |
| Scène 3D | Unity 6 (6000.6), pipeline URP, build WebGL |
| Tableau de bord | HTML, CSS, JavaScript (sans bibliothèque externe) |
| Pilotage de l'éditeur | Unity MCP officiel (travail de développement) |
| Tests | pytest (23 tests : modèle et API) |

### Normes et références

Le cadrage s'appuie sur les modèles de référence RAMI 4.0 (architecture de l'Industrie 4.0) et ISO 23247 (cadre des jumeaux numériques de fabrication). Nous les utilisons comme références de conception ; nous ne revendiquons pas de conformité formelle.

### Contraintes de sécurité

- le serveur écoute en local par défaut (`127.0.0.1`) ;
- les commandes reçues sont validées (poste existant, valeurs bornées : temps de cycle de 1 à 60 s, 1 à 3 machines, vitesse de 0,25 à 20) ;
- les données sont simulées : aucune donnée personnelle ni industrielle réelle ;
- limites connues : pas d'authentification, une seule simulation partagée entre tous les clients. À traiter avant toute exposition sur un réseau.

### Performances attendues

| Critère | Objectif | État |
|---|---|---|
| Fréquence de mise à jour de l'état | 10 Hz | mesuré (flux reçu à 10 Hz) |
| Affichage de la scène | 60 images par seconde, mouvement interpolé entre deux états | objectif de conception, à mesurer sur le PC de soutenance |
| Latence d'affichage | inférieure à 200 ms (retard d'interpolation de 150 ms) | conception |
| Reproductibilité des mesures | graine fixe (`SIM_SEED=7`) | réalisé |

## IV. Aspects organisationnels et humains

- **Formation des opérateurs :** le manuel utilisateur (`docs/manuel-utilisateur.md`) décrit la prise en main en quelques minutes : lire les KPI, tester un scénario, comparer.
- **Adaptation des processus :** le jumeau sert à décider (par exemple traiter le goulot) avant d'agir sur la ligne réelle.
- **Gestion du changement :** dans le cadre pédagogique, il s'agit surtout de partager un vocabulaire commun (cadence, goulot, en-cours, disponibilité) et d'impliquer chaque rôle dans les mesures.
- **Organisation de l'équipe :** voir les diapositives OBS et RACI de la présentation.

| Membre | Rôle |
|---|---|
| Aymane ISSAMI | 3D et architecture |
| Marouan EZBAKHE | 3D et animation |
| Hiba BENZAHRA | Simulation industrielle |
| Zaid HSAIN | Backend et communication |
| Houssam KICHCHOU | Dashboard et KPI |
| Mohamed HAMDOUCH | Optimisation et validation |

Le poste de chef de projet n'est pas encore désigné.

## V. Livrables attendus

| Livrable | Contenu | Statut |
|---|---|---|
| Prototype ou preuve de concept | simulation, scène 3D Unity WebGL, tableau de bord | scène et tableau de bord intégrés ; build WebGL à valider sur le PC de soutenance |
| Modèle de simulation | `backend/sim.py` et ses tests | fait |
| Interface synchronisée avec des données simulées | Unity + page web sur le même flux | fait |
| Scénario d'optimisation testé et documenté | `docs/resultats-unity.md` | mesuré avec la scène Unity |
| Documentation technique | `README.md`, `docs/protocole-websocket.md`, ce cahier des charges | fait |
| Manuel utilisateur | `docs/manuel-utilisateur.md` | à jour pour la version Unity |
| Rapport d'analyse des résultats | rapport LaTeX (`docs/rapport/`) | à mettre à jour pour la version Unity |

## VI. Planning et ressources

### Étapes (dates à fixer selon le calendrier de la soutenance)

1. Cadrage et cahier des charges.
2. Modèle de simulation et tests.
3. Protocole de communication, backend, historique.
4. Scène 3D Unity et client réseau.
5. Tableau de bord et page web.
6. Scénarios d'optimisation : mesures avec la scène Unity et analyse.
7. Rapport, manuel, présentation et soutenance.

### Ressources

| Type | Détail |
|---|---|
| Matériel | un PC par membre (Unity 6 demande une carte graphique récente) |
| Logiciels | Unity 6 Personal (gratuit), Python 3.9 ou plus, FastAPI, navigateur récent, Git |
| Budget | nul : outils gratuits |

## VII. Critères d'évaluation

| Critère | Indicateur de réussite |
|---|---|
| Atteinte des objectifs | les 3 livrables du sujet sont fournis ; 6 KPI suivis ; au moins 3 scénarios comparés |
| Respect des délais et du budget | livraison avant la soutenance ; budget nul |
| Qualité de la réalisation technique | 23 tests passent ; protocole documenté ; mesures reproductibles (graine fixe) |
| Facilité d'utilisation | prise en main décrite dans le manuel ; scène lisible (états en couleur, étiquettes en français) |
