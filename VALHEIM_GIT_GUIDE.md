# Valheim Git Quick Guide

Workspace:

C:\Users\ingva\OneDrive\Desktop\Dev\Valheim

This folder is the main Git repository for Valheim mod development.

## Current setup

- Git repository: Valheim
- Main branch: main
- Release ZIP files are ignored by Git
- README.md and .gitignore are tracked
- Initial baseline commit:

175cff5 Initialize Valheim mod development workspace

---

## Normal workflow

### Check what changed

git status

Always use this first.

### See exactly what changed

git diff

This shows line-by-line changes to tracked files.

### See previous save points

git log --oneline

---

## BEFORE changing a working mod

First:

git status

If the current mod version is working and there are changes that should be preserved:

git add .
git commit -m "LightMyFire: working state before new changes"

Then start experimenting.

This creates a safe save point.

---

## AFTER making changes

Check:

git status

Inspect:

git diff

Test the mod in Valheim.

If it works correctly:

git add .
git commit -m "LightMyFire: describe the completed change"

Example:

git add .
git commit -m "LightMyFire: fix barrel collision and range marker"

Do not label something as working until it has actually been tested.

---

## Release ZIP files

Files such as:

LightMyFire.zip
R4V9N1-EquipmentSheet-0.9.1.zip
R4V9N1-InventoryLink-0.2.6.zip
R4V9N1-Terramizer-0.9.0.zip

can remain in the Valheim directory.

They are ignored by Git.

Think of the workspace as:

ZIP files      = release shelf
Source folders = workshop
Git history    = time machine

---

## git add .

git add .

stages all non-ignored changes in the repository.

Before using it, run:

git status

so you know what is about to be included.

---

## Windows LF / CRLF warning

If Git says:

LF will be replaced by CRLF the next time Git touches it

that is normal on Windows.

It does not mean your file is damaged.

---

## The commands worth remembering

git status
git diff
git log --oneline

When a tested version is working:

git add .
git commit -m "Describe the working change"

---

## GOLDEN RULE

BEFORE CHANGING SOMETHING THAT CURRENTLY WORKS, COMMIT IT FIRST.

That gives us a clean recovery point if the next experiment creates another giant grey cube.
