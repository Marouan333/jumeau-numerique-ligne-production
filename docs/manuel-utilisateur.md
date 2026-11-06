# Manuel utilisateur – Jumeau numérique de la ligne de production

ENSAM Rabat · Mini-projet Industrie 4.0 · Prof. Maria Zemzami · 2026/2027

## 1. Démarrer

```bash
uv venv && uv pip install -r requirements.txt   # ou : python -m venv .venv && pip install -r requirements.txt
.venv/bin/python backend/server.py
```

Ouvrir <http://localhost:8000> dans un navigateur récent (Chrome, Edge, Firefox, Safari). Le badge **Connecté** en haut à gauche confirme la liaison WebSocket avec le backend. S'il affiche **Déconnecté**, le serveur est arrêté ; la page se reconnecte automatiquement dès qu'il redémarre.

## 2. L'écran

| Zone | Contenu |
|---|---|
| Barre supérieure | Horloge simulée, pause, vitesse (×1 à ×20), réinitialisation, vue globale, équipe |
| Scène 3D (gauche) | Ligne de production animée : 4 postes, convoyeur, produits, bac à rebuts, palette de produits finis |
| Panneau (droite) | Onglets **Supervision**, **Historique**, **Aide** |

**Naviguer dans la 3D** : clic gauche + glisser pour tourner, molette pour zoomer, clic droit + glisser pour déplacer. Un clic sur une machine la sélectionne et centre la caméra ; **Vue globale** recadre toute la ligne.

**Lire la scène** :

- voyant vert = en marche, gris = en attente, orange = bloqué (file aval pleine), rouge clignotant = en panne ;
- la barre sur la face avant de chaque machine montre l'avancement du produit en cours ;
- la couleur des produits indique leur stade : brun (brut), bleu (traité), vert (contrôlé), carton (emballé) ;
- les produits rejetés au contrôle tombent dans le bac rouge.

## 3. Onglet Supervision

- **KPI** : cadence réelle (sur la dernière minute simulée), cadence théorique (60 / temps du poste limitant), produits finis, rejets, en-cours, goulot d'étranglement et attente moyenne au contrôle.
- **État des postes** : état, longueur de file, taux d'utilisation, disponibilité, attente moyenne. Cliquer une ligne sélectionne le poste.
- **Débit de production** : courbe des 3 dernières minutes simulées, avec le débit théorique en pointillés.
- **Scénario d'optimisation** :
  - 4 scénarios prêts à l'emploi (chacun relance la simulation à zéro et crée une nouvelle exécution dans l'historique) ;
  - curseurs des temps de contrôle et de traitement, nombre de postes de contrôle, convoyeur adaptatif et pannes aléatoires (appliqués en direct).
- **Machine sélectionnée** : provoquer ou réparer une panne, statistiques du poste (utilisation, disponibilité, temps bloqué, nombre de pannes, MTBF et MTTR observés).
- **Comparaison avant / après** :
  - simule 1 h de chaque scénario dans les mêmes conditions (même graine aléatoire) et affiche les écarts par rapport à la situation initiale ;
  - **Exporter CSV** télécharge le tableau.
- **Journal** : pannes, remises en service, changements de réglages.

## 4. Onglet Historique

Chaque exécution (démarrage, scénario appliqué, remise à zéro) est conservée dans la base SQLite `data/jumeau.db`. Un relevé des KPI est enregistré à chaque minute simulée.

- choisir une exécution dans la liste ;
- **Synthèse** : durée, production, cadence, en-cours maximal, nombre de pannes, goulot ;
- courbes du débit et de l'en-cours, utilisation moyenne des postes, liste des événements ;
- **Exporter cette exécution (CSV)** ou **Tout exporter** (séparateur `;`, ouvrable dans Excel).

## 5. Démonstration type (5 minutes)

1. Situation initiale, vitesse ×10 : observer la file qui s'accumule devant le contrôle qualité. Le goulot affiché est « Contrôle qualité » et la cadence plafonne vers 4 produits/min.
2. Sélectionner le traitement et **Provoquer une panne** : le voyant passe au rouge, l'amont se bloque, la cadence chute.
3. Appliquer **Contrôle automatisé (8 s)** : la file se résorbe, la cadence monte vers 6,5 à 7,5 /min et le goulot passe au traitement.
4. **Lancer la comparaison** : tableau avant/après des 4 scénarios et des réglages actuels.
5. Onglet **Historique** : comparer les courbes des deux exécutions, puis exporter en CSV.

## 6. Problèmes fréquents

| Symptôme | Solution |
|---|---|
| Scène 3D vide | Three.js est chargé depuis un CDN : vérifier la connexion Internet |
| « Déconnecté » | Relancer `backend/server.py`. Le port 8000 est peut-être occupé : `PORT=8010 python backend/server.py` |
| Historique vide | Les relevés sont pris chaque minute simulée : attendre ou accélérer (×20) |
| Remettre l'historique à zéro | Arrêter le serveur et supprimer `data/jumeau.db` |
