---
created: 2026-11-22 16:35:17
---

# Stop PluginRunnerTests timing out when the machine is loaded

On 2026-11-22 the commit gate (bash .claude/commit-checks.sh) failed once: PluginRunnerTests.Rewrite_CarriesCyrillicAndEmojiBothWays got Failed instead of Answered after 11 s. That run of the suite took 1 m 42 s; the plugin tests then passed alone (39/39, 19 s), and a full re-run passed 289/289 in 16 s. The fixture helper Fixture() in tests/PluginRunnerTests.cs defaults timeoutSeconds to 10, so a slow Node start under load trips the plugin timeout. Next step: confirm the failure reason was the timeout (print ReasonOf on failure), then raise the default fixture timeout (the hang test passes timeoutSeconds: 1 explicitly, so it is unaffected).

Resolved 2026-11-27: AnswerOf in tests/PluginRunnerTests.cs now fails with the plugin's Reason instead of a bare type mismatch, and every fixture installed through TempPlugins.InstallFixture without an explicit deadline gets TempPlugins.FixtureTimeoutSeconds (60). The committed fixture-rewrite/plugin.json, which InstallableRewriteFixture_RunsFromItsOwnFolder runs through Node, sets timeoutSeconds to the same 60. CPU load alone did not reproduce the failure: with 160 busy loops on 32 cores the full suite took 1 m 15 s and still passed. The cause is read off the 11 s elapsed time, since every other Failed reason returns at once. If the failure comes back, the message will name the reason.
