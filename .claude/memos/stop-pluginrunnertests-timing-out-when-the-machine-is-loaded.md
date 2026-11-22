---
created: 2026-11-22 16:35:17
---

# Stop PluginRunnerTests timing out when the machine is loaded

On 2026-11-22 the commit gate (bash .claude/commit-checks.sh) failed once: PluginRunnerTests.Rewrite_CarriesCyrillicAndEmojiBothWays got Failed instead of Answered after 11 s. That run of the suite took 1 m 42 s; the plugin tests then passed alone (39/39, 19 s), and a full re-run passed 289/289 in 16 s. The fixture helper Fixture() in tests/PluginRunnerTests.cs defaults timeoutSeconds to 10, so a slow Node start under load trips the plugin timeout. Next step: confirm the failure reason was the timeout (print ReasonOf on failure), then raise the default fixture timeout (the hang test passes timeoutSeconds: 1 explicitly, so it is unaffected).
