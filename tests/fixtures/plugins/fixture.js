// A test plugin for the url-cleaner host. The first argument picks what it does, and the second is that mode's
// parameter. PluginRunnerTests drives every mode; the fixture-* folders beside this file install a few of them as real
// plugins for manual checks.
const fs = require("fs");
const { spawn } = require("child_process");

const [mode, parameter] = process.argv.slice(2);
const readRequest = () => JSON.parse(fs.readFileSync(0, "utf8"));
const answer = (value) => process.stdout.write(typeof value === "string" ? value : JSON.stringify(value));

switch (mode) {
  case "none":
    readRequest();
    answer({ type: "none" });
    break;
  case "rewrite": {
    const request = readRequest();
    answer({ type: "rewrite", text: `${request.text} (rewritten)` });
    break;
  }
  case "notify": {
    const request = readRequest();
    answer({ type: "notify", message: `Noted ${request.text}` });
    break;
  }
  case "error":
    readRequest();
    answer({ type: "error", message: "The fixture refused" });
    break;
  case "confirm": {
    // The parameter, when given, delays the answer to the action call by that many milliseconds.
    const request = readRequest();
    if (request.type === "action") {
      const done = () => answer({ type: "notify", message: `${request.action}: ${request.fields.folder} (${request.state.articleId})` });
      if (parameter) setTimeout(done, Number(parameter));
      else done();
    } else {
      answer({
        type: "confirm",
        title: "Save article",
        message: request.text,
        fields: [
          { id: "folder", label: "Folder", value: "Reading list", options: ["Reading list", "Archive"] },
          { id: "tags", label: "Tags", value: "" },
        ],
        actions: [{ id: "save", label: "Save" }],
        state: { articleId: 1234 },
      });
    }
    break;
  }
  case "raw":
    // The parameter is written to stdout exactly as given.
    readRequest();
    answer(parameter);
    break;
  case "exit-3":
    // Exits without reading the request.
    process.exitCode = 3;
    break;
  case "hang": {
    // Starts a child that never ends, records its own pid and the child's in the file named by the parameter, and
    // never answers.
    const child = spawn(process.execPath, ["-e", "setInterval(() => {}, 1000)"], { windowsHide: true, stdio: "ignore" });
    fs.writeFileSync(parameter, `${process.pid} ${child.pid}`);
    setInterval(() => {}, 1000);
    break;
  }
  case "stderr-flood":
    // Fills stderr before reading the request, which deadlocks a host that writes stdin before it reads stderr.
    process.stderr.write("x".repeat(2 * 1024 * 1024));
    readRequest();
    answer({ type: "none" });
    break;
  case "stdout-flood": {
    // Writes to stdout without end, never answering.
    const chunk = "x".repeat(1024 * 1024);
    for (;;) process.stdout.write(chunk);
  }
  case "emoji-stderr":
    // Writes emoji to stderr past the host's tail length, so the tail's cut can land inside one, then answers none.
    process.stderr.write("\u{1F3AC}".repeat(1001) + "a");
    readRequest();
    answer({ type: "none" });
    break;
  case "marker":
    // Appends a line to the file named by the parameter, so a test can count its runs, and answers notify.
    fs.appendFileSync(parameter, "ran\n");
    readRequest();
    answer({ type: "notify", message: "Marked" });
    break;
  case "slow": {
    // Answers after the parameter's number of milliseconds: with the third argument written exactly as given, or with
    // notify when there is none.
    const [, , custom] = process.argv.slice(2);
    setTimeout(() => {
      readRequest();
      answer(custom ?? { type: "notify", message: "Done slowly" });
    }, Number(parameter));
    break;
  }
  default:
    process.stderr.write(`unknown mode ${mode}`);
    process.exitCode = 2;
}
