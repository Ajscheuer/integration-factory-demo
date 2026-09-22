# Engineering policy — integration connectors

The policy is an *input* to the AI (slide 08), not a wiki page. Sections are extracted by the CLI into the mapping and
implementation briefs by their `##` heading, so keep the headings stable.

## Mapping

- A canonical field is mapped from exactly one provider expression. If two provider fields could feed it, pick one and
  say why in `notes`; do not merge silently.
- Provider sentinels (`unknown`, `n/a`, `none`, empty string, `null`) never reach the product as strings. Numeric targets
  become `null`, list targets become empty, enum targets become the canonical `Unknown` member. State the sentinel
  handling explicitly in `transform`.
- Identifiers: the product never stores provider URLs as ids. Extract the stable id (for SWAPI: last non-empty path
  segment of `url`), keep the URL in `sourceUrl`.
- Enumerations are mapped value-by-value. Any provider value that has no obvious canonical member is a business
  question: `requiresHumanApproval: true` + an entry in `unresolvedQuestions`.
- Numbers with units or calendars (BBY/ABY years, "1,358" masses, "1 standard" gravity) are ambiguous by default:
  confidence ≤ 0.7 unless the product contract states the convention.
- Confidence is your honest estimate that two engineers would implement the same thing from your `transform`.
- Never propose calling endpoints outside the ones listed in the brief.

## Implementation

- All provider traffic goes through the generated client (`I{Provider}Client`). Never `new HttpClient()`, never raw URLs.
- Only call the client methods the brief allows. Adding an endpoint is a manifest change and a new mapping, not a code change.
- Errors are translated at the connector boundary into `ConnectorException` with a stable `Code`:
  `not_found` (404), `unauthorized` (401/403), `provider_unavailable` (408/429/5xx, timeouts, network),
  `invalid_response` (deserialization / contract violations). Retries for 429/5xx are handled by the resilience handler in DI;
  do not add retry loops in the connector.
- Mapping code is pure: static, no I/O, no logging, no DateTime.Now. Parsing helpers are `private static` in the mapper.
- Every human decision recorded in the mapping file is honoured literally and covered by a test.
- Cancellation tokens are forwarded to every client call.
- Pagination: expose the provider's cursor as an opaque `nextPageToken` (base64 of the provider `next` URL); never leak page numbers.

## Security

- No credentials in code, tests, fixtures or briefs. Secrets are referenced by `SecretName` and resolved from configuration.
- Never log provider payloads, identifiers or request bodies. Log operation name + `Code` only.
- No new NuGet packages beyond the generated project's baseline without a manifest change and human approval.
- Fixtures must be recorded from public sandbox data only.

## Review

- Reviewers verify the diff against the approved mapping, not against their own mapping opinion. Disagreement with a
  mapping is raised on the mapping file, not fixed in code.
- Any remaining `TODO(ai)` marker, undocumented endpoint or unapproved low-confidence mapping is a release blocker.
