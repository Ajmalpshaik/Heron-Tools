# Security Policy

## Reporting a vulnerability

**Do not open a public issue for a security problem.**

Report it privately through **[GitHub Security Advisories](https://github.com/Ajmalpshaik/Heron-Tools/security/advisories/new)**.
If that is unavailable, contact the maintainer, [@Ajmalpshaik](https://github.com/Ajmalpshaik), directly and
mark the message as a security report.

Please include what the problem is, what it could cause, the steps to reproduce, and your Revit and
Heron Tools versions. You will get an acknowledgement within a few days. Anything that can damage a model
is fixed before everything else.

## What counts as a security issue in Heron Tools

- Anything that can change or delete model content the modeller did not ask to change.
- Anything that writes outside Heron Tools' own folders (`%AppData%\Autodesk\Revit\Addins\<year>\HeronTools`,
  `%AppData%\HeronTools`).
- Anything that loads code or files from outside the installed add-in.
- Build or install scripts that could delete files other than Heron Tools' own.

## Design rules that exist to prevent these

- Every model change is one transaction, rolled back on any failure; the commit result is checked.
- Tools that write graphic overrides remember the originals and can put them back.
- Heron Tools makes no network calls and sends nothing anywhere.
- The install and uninstall scripts touch only `HeronTools.addin` and the `HeronTools` folder of each Revit
  year, and refuse to work through a linked (junction) folder.

## Reporting accidentally committed data

If a client model, drawing, name or any project data was committed by mistake, report it privately as above.
It will be removed from the repository history, not only from the latest version.

## Supported versions

| Version | Supported |
|---|---|
| 1.0.x (current, `main`) | Yes |
| Anything older | No |
