# Provider-neutral toasting protocol

Toaster does not require a built-in generative model. Semantic distillation is exposed as work that any connected model/provider can perform.

## Why

The user may already be working through ChatGPT, Claude, Gemini, an IDE agent, a local model, or another harness. Requiring a second dedicated model inside Toaster would add cost, configuration, and lock-in while often duplicating intelligence that is already present in the active agent.

Instead, Toaster separates deterministic memory infrastructure from semantic work.

## Job kinds

### `source-section`

Created from an ingested manual/research section. The job context includes source metadata, the stable section id, heading/page identity, and extracted text.

The provider should extract reusable operational knowledge, not summarize the page merely because it was asked to process it.

Useful outputs include:

- constraints;
- techniques;
- compatibility facts;
- warnings;
- failure modes;
- diagnostic clues;
- procedures or heuristics;
- important exceptions.

### `live-observation`

Created when an agent records something that happened during real work. The context contains the activity, attempted approach, result, resolution if known, and environment information.

The provider should generalize only what can reasonably be reused later.

Example observation:

```json
{
  "activity": "Generate manifold Blender mesh",
  "attempt": "Boolean-union coplanar branch meshes",
  "result": "Non-manifold output",
  "resolution": "Voxel remesh followed by cleanup",
  "environmentJson": "{\"Blender\":\"4.x\"}"
}
```

Possible toast:

```json
{
  "title": "Repair Boolean-heavy procedural meshes with remeshing",
  "statement": "When procedural Boolean unions leave coplanar/internal geometry and non-manifold output, voxel remeshing followed by cleanup can produce a printable manifold result.",
  "conditions": ["Topology precision is less important than watertight output"],
  "exceptions": ["Voxel remeshing may destroy fine detail or intentional topology"],
  "confidence": 0.65
}
```

## MCP loop

A compatible agent can use this loop:

1. `toaster_get_toasting_jobs`
2. analyze each returned job using its current model
3. `toaster_submit_toasting_result`
4. repeat when appropriate

For live work, the agent can call `toaster_add_observation`. Unless `queueForToasting` is false, the response includes the newly created toasting job, allowing the same model to process the lesson immediately while context is fresh.

## Candidate schema

Each submitted candidate may contain:

```json
{
  "title": "Short useful title",
  "statement": "The compact operational lesson",
  "explanation": "Optional deeper explanation",
  "domains": ["optional", "domains"],
  "tags": ["optional", "tags"],
  "conditions": ["conditions where it applies"],
  "exceptions": ["known exceptions"],
  "confidence": 0.7
}
```

A provider may submit an empty candidate list. **Not every piece of source material deserves toast.** This is preferable to manufacturing generic summaries.

## Provenance

Toaster, not the provider, creates provenance records from the job metadata.

For manual distillation, resulting toast links back to the exact source and section/page used as evidence.

For live distillation, provenance records the originating observation id in the provenance note.

Provider identity may be added as optional audit metadata later, but it is intentionally not required by the core protocol.

## User control and cost

Queuing a toasting job performs no model inference and spends no provider tokens by itself.

The connected agent/provider decides when jobs are processed. This permits workflows such as:

- toast a manual immediately after import;
- leave manuals indexed but undistilled;
- process jobs only when relevant to current work;
- use a cheap/local model for bulk manual distillation;
- use the currently active high-capability model for live lessons;
- reprocess selected evidence with a different provider later.

## Distillation rule

The canonical instruction is intentionally strict:

> Extract reusable application-specific operational lessons from the supplied evidence. Do not merely summarize it and do not preserve project-specific state. Capture techniques, constraints, compatibility facts, failure patterns, successful remedies, warnings, or useful heuristics. Include applicability conditions and exceptions where known. It is valid to return zero candidates if there is no reusable lesson. Never invent evidence that is not present.

This boundary is central to Toaster: the system is intended to accumulate **expertise**, not simply ever-growing summaries.
