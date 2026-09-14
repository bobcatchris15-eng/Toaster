# Toaster product specification

## Purpose

Toaster is a Windows-native, local-first expertise store for AI agents. It acquires research and implementation experience, distills that material into reusable application-specific lessons, and exposes those lessons cheaply to later agents.

## Knowledge structures

### Toast Graph

Operational knowledge: techniques, constraints, warnings, failure lessons, successful approaches, compatibility facts, environment quirks, heuristics, diagnostics, and tradeoffs.

### Encyclopedia Graph

A semantic/index structure over retained source documents. It answers whether Toaster already contains material likely to answer a question and which small portion should be inspected before a full source is loaded.

### Provenance

Derived knowledge must remain traceable to sources or observations. Contradictions and historical/version-specific knowledge are retained rather than flattened into one supposedly canonical truth.

## Retrieval ladder

cheap toast lookup → source previews → relevant source sections → complete source

## Model boundary

Graph storage, source persistence, provenance, ranking, and retrieval do not require a generative model. LLMs are replaceable semantic workers used for distillation and reconciliation where useful.

## User experience

Windows is a primary platform. The baseline application is installable by an ordinary user, registers normally with Windows, starts its local service automatically, provides a tray/config application, and gives explicit copy/paste integration details for external agent apps.

## Deferred from v0.1

Autonomous web browsing, cloud synchronization, distributed federation, multi-user support, mandatory hosted AI, and coupling to any specific external agent system.
