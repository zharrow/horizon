# Horizon

Tu es un trou noir. Si tu arrêtes de manger, tu t'évapores.

- `horizon.html` : le prototype web d'origine (s'ouvre directement dans un navigateur).
- `Horizon/` : le jeu refait en Unity pour iOS et Android.

## Reprendre le projet Unity

1. Ouvrir `Horizon/` dans Unity Hub avec **Unity 6000.6.3f1**. La première ouverture régénère le dossier `Library` (quelques minutes).
2. Ouvrir la scène `Assets/Horizon/Horizon.unity`, puis Play.

Pour publier : ajouter les modules **iOS Build Support** et **Android Build Support** dans Unity Hub, et installer Xcode.

## Organisation

Tout le jeu est dans `Horizon/Assets/Horizon/` :

- `Scripts/HorizonData.cs` : tables d'équilibrage (ères, objets, améliorations).
- `Scripts/HorizonGame*.cs` : boucle et entrées, simulation, rendu, interface.
- `Scripts/Draw2D.cs` et `Resources/Shaders/` : dessin de la scène et post-traitement.
- `Scripts/Synth.cs` : sons générés par le code, sans fichiers audio.
- `Scripts/Haptics.cs` et `Assets/Plugins/iOS/` : vibrations.
- `Resources/Horizon.uss` : styles de l'interface (UI Toolkit).

## Feuille de route

1. Portage du prototype : fait.
2. Firebase Analytics et Remote Config (l'équilibrage de `HorizonData.cs` passe en Remote Config).
3. Achats intégrés (Unity IAP).
4. Bêta TestFlight et piste interne Google Play.
