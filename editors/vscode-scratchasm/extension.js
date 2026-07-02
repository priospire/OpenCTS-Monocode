const vscode = require("vscode");
const cp = require("child_process");
const fs = require("fs");
const path = require("path");

let client;
let diagnostics;

function activate(context) {
  diagnostics = vscode.languages.createDiagnosticCollection("ScratchASM");
  context.subscriptions.push(diagnostics);

  client = new ScratchAsmClient(context.extensionPath, diagnostics);
  client.start();

  context.subscriptions.push(vscode.workspace.onDidOpenTextDocument(document => client.open(document)));
  context.subscriptions.push(vscode.workspace.onDidChangeTextDocument(event => client.change(event.document)));
  context.subscriptions.push(vscode.workspace.onDidCloseTextDocument(document => diagnostics.delete(document.uri)));
  context.subscriptions.push(vscode.commands.registerCommand("scratchasm.restartLanguageHost", () => {
    client.dispose();
    diagnostics.clear();
    client = new ScratchAsmClient(context.extensionPath, diagnostics);
    client.start();
    for (const document of vscode.workspace.textDocuments) {
      client.open(document);
    }
  }));
  context.subscriptions.push(vscode.languages.registerCompletionItemProvider(
    { language: "scratchasm" },
    {
      provideCompletionItems(document, position) {
        return client.completion(document, position);
      }
    },
    ".",
    "(",
    "\"",
    "`"
  ));

  for (const document of vscode.workspace.textDocuments) {
    client.open(document);
  }
}

function deactivate() {
  if (client) {
    client.dispose();
  }
}

class ScratchAsmClient {
  constructor(extensionPath, diagnosticCollection) {
    this.extensionPath = extensionPath;
    this.diagnostics = diagnosticCollection;
    this.sequence = 1;
    this.pending = new Map();
    this.buffer = Buffer.alloc(0);
    this.process = undefined;
    this.ready = false;
  }

  start() {
    const command = resolveLanguageHost(this.extensionPath);
    if (!command) {
      return;
    }

    this.process = cp.spawn(command.command, command.args, {
      cwd: command.cwd,
      stdio: ["pipe", "pipe", "pipe"],
      windowsHide: true
    });
    this.process.stdout.on("data", chunk => this.accept(chunk));
    this.process.stderr.on("data", chunk => console.warn(chunk.toString()));
    this.process.on("exit", () => {
      this.ready = false;
      this.process = undefined;
      for (const reject of this.pending.values()) {
        reject(new Error("ScratchASM language host exited."));
      }
      this.pending.clear();
    });

    this.request("initialize", {}).then(() => {
      this.ready = true;
      for (const document of vscode.workspace.textDocuments) {
        this.open(document);
      }
    }).catch(error => console.warn(error.message));
  }

  dispose() {
    if (this.process) {
      this.process.kill();
    }
    this.process = undefined;
    this.ready = false;
    this.pending.clear();
  }

  open(document) {
    if (!this.ready || document.languageId !== "scratchasm") {
      return;
    }

    this.notify("textDocument/didOpen", {
      textDocument: {
        uri: document.uri.toString(),
        languageId: "scratchasm",
        version: document.version,
        text: document.getText()
      }
    });
  }

  change(document) {
    if (!this.ready || document.languageId !== "scratchasm") {
      return;
    }

    this.notify("textDocument/didChange", {
      textDocument: {
        uri: document.uri.toString(),
        version: document.version
      },
      contentChanges: [
        { text: document.getText() }
      ]
    });
  }

  async completion(document, position) {
    if (!this.ready || document.languageId !== "scratchasm") {
      return [];
    }

    const result = await this.request("textDocument/completion", {
      textDocument: { uri: document.uri.toString() },
      position: { line: position.line, character: position.character }
    });
    return (Array.isArray(result) ? result : []).map(item => {
      const completion = new vscode.CompletionItem(item.label, toCompletionKind(item.kind));
      completion.insertText = item.insertText || item.label;
      completion.detail = item.detail || "";
      return completion;
    });
  }

  notify(method, params) {
    this.write({ jsonrpc: "2.0", method, params });
  }

  request(method, params) {
    const id = this.sequence++;
    const message = { jsonrpc: "2.0", id, method, params };
    return new Promise((resolve, reject) => {
      this.pending.set(id, { resolve, reject });
      this.write(message);
    });
  }

  write(message) {
    if (!this.process || !this.process.stdin.writable) {
      return;
    }

    const payload = Buffer.from(JSON.stringify(message), "utf8");
    this.process.stdin.write(`Content-Length: ${payload.length}\r\n\r\n`);
    this.process.stdin.write(payload);
  }

  accept(chunk) {
    this.buffer = Buffer.concat([this.buffer, chunk]);
    while (true) {
      const headerEnd = this.buffer.indexOf("\r\n\r\n");
      if (headerEnd < 0) {
        return;
      }

      const header = this.buffer.slice(0, headerEnd).toString("ascii");
      const match = /Content-Length:\s*(\d+)/i.exec(header);
      if (!match) {
        this.buffer = this.buffer.slice(headerEnd + 4);
        continue;
      }

      const length = Number(match[1]);
      const total = headerEnd + 4 + length;
      if (this.buffer.length < total) {
        return;
      }

      const payload = this.buffer.slice(headerEnd + 4, total).toString("utf8");
      this.buffer = this.buffer.slice(total);
      this.dispatch(JSON.parse(payload));
    }
  }

  dispatch(message) {
    if (message.method === "textDocument/publishDiagnostics") {
      this.publishDiagnostics(message.params);
      return;
    }

    if (Object.prototype.hasOwnProperty.call(message, "id")) {
      const pending = this.pending.get(message.id);
      if (!pending) {
        return;
      }

      this.pending.delete(message.id);
      if (message.error) {
        pending.reject(new Error(message.error.message || "ScratchASM language host request failed."));
      } else {
        pending.resolve(message.result);
      }
    }
  }

  publishDiagnostics(params) {
    const uri = vscode.Uri.parse(params.uri);
    const items = (params.diagnostics || []).map(diagnostic => new vscode.Diagnostic(
      new vscode.Range(
        diagnostic.range.start.line,
        diagnostic.range.start.character,
        diagnostic.range.end.line,
        diagnostic.range.end.character
      ),
      diagnostic.message,
      diagnostic.severity === 1 ? vscode.DiagnosticSeverity.Error : vscode.DiagnosticSeverity.Warning
    ));
    this.diagnostics.set(uri, items);
  }
}

function resolveLanguageHost(extensionPath) {
  const configured = vscode.workspace.getConfiguration("scratchasm").get("languageHostPath");
  if (configured && fs.existsSync(configured)) {
    return toCommand(configured, process.cwd());
  }

  const repoRoot = path.resolve(extensionPath, "..", "..");
  const dll = path.join(repoRoot, "src", "ScratchASM.LanguageHost", "bin", "Debug", "net10.0", "ScratchASM.LanguageHost.dll");
  if (fs.existsSync(dll)) {
    return { command: "dotnet", args: [dll, "--lsp"], cwd: repoRoot };
  }

  const exe = path.join(repoRoot, "ScratchASM.LanguageHost.exe");
  if (fs.existsSync(exe)) {
    return { command: exe, args: ["--lsp"], cwd: repoRoot };
  }

  const project = path.join(repoRoot, "src", "ScratchASM.LanguageHost", "ScratchASM.LanguageHost.csproj");
  if (fs.existsSync(project)) {
    return { command: "dotnet", args: ["run", "--project", project, "--", "--lsp"], cwd: repoRoot };
  }

  vscode.window.showWarningMessage("ScratchASM language host was not found. Set scratchasm.languageHostPath for diagnostics and completions.");
  return undefined;
}

function toCommand(hostPath, cwd) {
  if (hostPath.toLowerCase().endsWith(".dll")) {
    return { command: "dotnet", args: [hostPath, "--lsp"], cwd };
  }

  return { command: hostPath, args: ["--lsp"], cwd };
}

function toCompletionKind(kind) {
  switch (kind) {
    case 3:
      return vscode.CompletionItemKind.Function;
    case 6:
      return vscode.CompletionItemKind.Variable;
    case 7:
      return vscode.CompletionItemKind.Class;
    case 13:
      return vscode.CompletionItemKind.Enum;
    case 14:
      return vscode.CompletionItemKind.Keyword;
    default:
      return vscode.CompletionItemKind.Text;
  }
}

module.exports = {
  activate,
  deactivate
};
