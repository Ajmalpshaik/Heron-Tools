# Autodesk App Store (Design and Make Marketplace)

[README](../../README.md) > Store

What Heron Tools needs to be listed on the Autodesk App Store, now called the Design and Make Marketplace.
Checked on 2026-11-22 against Autodesk's Revit publisher guidelines and their Desktop App Submission
Process Overview. Autodesk's Publisher Agreement wins if it says something different.

**Status:** not submitted. The Store version will leave out **Model Walk** until that tool is finished.

| Piece | State |
|---|---|
| Privacy policy | Written: [`PRIVACY.md`](../../PRIVACY.md). Public link after merge: https://github.com/Ajmalpshaik/Heron-Tools/blob/main/PRIVACY.md |
| Privacy + licence links inside the add-in | Done in the About Heron window (builds clean; the real window is not yet seen in Revit) |
| Help file | Written: [`help/HeronTools-Help.html`](help/HeronTools-Help.html), no Model Walk; the Store zip script picks it up |
| Listing text | Draft: [`listing.md`](listing.md), waiting for Ajmal's wording changes |
| Screenshots | Not taken (ideas in `listing.md`, Ajmal takes them in Revit) |
| Store build and zip | Another session |

## Store icon

| | |
|---|---|
| File | [`heron-tools-store-icon-120.png`](heron-tools-store-icon-120.png) |
| Made from | [`docs/brand/heron-app-icon-orange.png`](../brand/heron-app-icon-orange.png) (512 x 512), scaled to 120 x 120 |
| Why orange | The same icon as the installer (`build/make-installer-icon.ps1`), so the Store, the installer and the ribbon match |

Autodesk's icon rules: about 120 x 120 pixels, PNG, GIF or JPG, under 2 MB. It should be a picture that says
something about the app, clear at small size, with a border or frame, and with no app or company name on it
(the Store shows the name beside the icon).

## Listing rules

| Item | Rule |
|---|---|
| Screenshots | Up to 10, each at most 2000 x 2000 px and 2 MB, PNG/GIF/JPG, with a caption under each |
| Video | Optional link to YouTube or Autodesk Screencast |
| Description | Up to 4,000 characters; bullets and links are allowed |
| App file | One zip (max 600 MB) holding the add-in files and a help file (txt, doc, html or pdf). Autodesk builds the installer from it |
| Help | Autodesk makes a standard quick-start help page from the form; our own help is extra |
| Privacy | A link to a privacy policy in the description and inside the add-in |
| Support | The listing must say how to get support (an email or a web page) |
| Revit | Must work in Revit 2027; 2022 to 2026 may be listed too |
| Price | Free: no PayPal account needed |
| Review | Up to 2 weeks. Every later version is reviewed again |
