# REPOrt

REPOrt est un plugin BepInEx 5 pour R.E.P.O. Appuie sur **F8** en jeu pour ouvrir ou fermer le menu.

## Installation sous Windows

1. Installe BepInEx 5 dans le dossier qui contient `REPO.exe`. Si tu utilises un gestionnaire de mods, ouvre le dossier du profil BepInEx utilisé pour lancer le jeu.
2. Copie [REPOrt.dll](build/REPOrt.dll) dans `BepInEx/plugins/`.
3. Lance le jeu avec ce profil et appuie sur **F8**.

## Commandes

| Onglet | Fonctions |
| --- | --- |
| Settings | Enregistrer, recharger ou réinitialiser la configuration. Activer le journal de débogage et régler la taille du menu et des étiquettes ESP. |
| Self Cheats | Endurance illimitée, invulnérabilité, protection contre les chutes et le ciblage ennemi, batterie illimitée et nombre de projectiles pour l’objet tenu. |
| Visuals | Régler le champ de vision et afficher les joueurs, ennemis, objets, chariots, extractions, têtes de joueurs morts et camion. **Toggle ESP** commande l’affichage global. **Toggle All ESP** sélectionne ou désélectionne toutes les catégories. |
| Player | Voir les PV actuels et maximum de chaque joueur, le soigner ou lui infliger le montant de dégâts saisi. |
| Enemy | Voir le nombre d’ennemis, leurs PV et leur cible. Geler ou libérer un ennemi, le tuer ou tuer tous les ennemis actifs. |

Les actions sur les joueurs et les ennemis ainsi que la protection contre le ciblage nécessitent de jouer en solo ou d’héberger la partie. Pour conserver tes réglages, clique sur **Save Settings**. Le fichier de configuration est `BepInEx/config/fr.rangedace.report.cfg`.

## Compilation

Depuis le dossier du projet, exécute la commande suivante dans PowerShell en adaptant le chemin à ton installation.

```powershell
.\build.ps1 -GameDir 'D:\Programmes\Steam\steamapps\common\REPO'
```

Le script utilise le compilateur .NET Framework de Windows et les DLL du jeu. Si BepInEx est installé dans un profil séparé, ajoute `-CoreDir 'chemin\du\profil\BepInEx\core'`. La DLL produite se trouve dans `build/REPOrt.dll`.

La DLL fournie a été compilée contre l’installation locale du jeu. Son fonctionnement en partie reste à vérifier.
