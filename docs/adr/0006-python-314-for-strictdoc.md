# ADR-0006: Python 3.14 jen pro StrictDoc

- Stav: Přijato
- Datum: 2026-11-04

## Kontext

StrictDoc 0.30.4 oficiálně podporuje Python 3.10 až 3.14. Na Pythonu 3.15 se nedá
nainstalovat (pro 3.15 neexistují wheely závislostí, například `tree-sitter==0.25.2`).
Na vývojovém stroji je jinak jediný Windows Python 3.15.

## Rozhodnutí

StrictDoc běží ve virtuálním prostředí `.venv` vytvořeném z Pythonu 3.14
(`py -3.14 -m venv .venv`). Verze je připíchnutá v `tools/requirements-docs.txt`.
Venv není v gitu.

## Důsledky

- Na stroji jsou dvě verze Pythonu, 3.14 jen jako runtime tohoto nástroje.
- Až StrictDoc podpoří Python 3.15, přechod je jedním příkazem
  (`py uninstall 3.14`, nové venv) a tento ADR se nahradí.
- Žádný produkční kód CTA na Pythonu nezávisí, jde jen o vývojový nástroj.
