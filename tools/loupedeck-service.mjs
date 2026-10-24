#!/usr/bin/env node

import fs from "node:fs";
import os from "node:os";
import path from "node:path";

const [, , name, parametersArg = "{}"] = process.argv;
const parametersJson = process.env.LOUPEDECK_PARAMETERS || parametersArg;
if (!name) {
  console.error("Usage: node loupedeck-service.mjs <request-name> [parameters-json]");
  process.exit(2);
}

let parameters;
try {
  parameters = JSON.parse(parametersJson);
} catch (error) {
  console.error(`Invalid parameters JSON: ${error.message}`);
  process.exit(2);
}

const portFile = path.join(
  os.homedir(),
  "AppData",
  "Local",
  "Logi",
  "LogiPluginService",
  "Temp",
  "WebSocketPort.txt",
);
const port = fs.readFileSync(portFile, "utf8").trim();
const request = {
  id: Math.floor(Date.now() % 1_000_000_000),
  name,
  parameters,
};

const socket = new WebSocket(`ws://localhost:${port}/configui3`);
let completed = false;
const timer = setTimeout(() => {
  console.error(`Timed out waiting for ${name}`);
  socket.close();
  process.exit(1);
}, 15_000);

socket.addEventListener("open", () => socket.send(JSON.stringify(request)));
socket.addEventListener("message", (event) => {
  let response;
  try {
    response = JSON.parse(String(event.data));
  } catch {
    return;
  }
  if (response.id !== request.id) return;
  completed = true;
  clearTimeout(timer);
  console.log(JSON.stringify(response, null, 2));
  socket.close();
  if (response.failed) process.exitCode = 1;
});
socket.addEventListener("error", (event) => {
  if (completed) return;
  clearTimeout(timer);
  console.error(event.message || "WebSocket connection failed");
  process.exit(1);
});
