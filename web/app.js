"use strict";

const $ = id => document.getElementById(id);
const names = { cards: "CardsNX", homenx: "HomeNX" };

let found = null;       // { label, drive, installed } while a Switch is plugged in
let working = false;
let injected = null;    // what the last inject reported, while the popup asks

// ---- host bridge -----------------------------------------------------------

let seq = 0;
const waiting = new Map();

function call(cmd, args) {
  return new Promise((ok, fail) => {
    const id = ++seq;
    waiting.set(id, { ok, fail });
    chrome.webview.postMessage(Object.assign({ id, cmd }, args));
  });
}

chrome.webview.addEventListener("message", e => {
  const m = e.data;
  if (m.type === "progress") return progress(m);
  const w = waiting.get(m.id);
  if (!w) return;
  waiting.delete(m.id);
  if (m.ok) w.ok(m.data); else w.fail(new Error(m.error));
});

// ---- page ------------------------------------------------------------------

function say(text, tone) {
  $("status").textContent = text;
  $("status").dataset.tone = tone || "";
}

function render() {
  $("device").dataset.state = found ? "found" : "looking";
  $("deviceTitle").textContent = found ? "Switch found" : "Looking for your Switch…";
  $("deviceText").textContent = found ? found.label + (found.installed ? " · the frontend is on" : "") : "";
  $("howto").hidden = !!found;
  $("go").disabled = working || !found;
  $("off").hidden = working;
  $("off").disabled = !found || !found.installed;
}

function progress(m) {
  const part = m.total > 0 ? m.done / m.total : 0;
  // The download is the short leg, the copy the long one.
  $("fill").style.width = (m.step === "download" ? part * 30 : 30 + part * 70) + "%";
  $("bar").hidden = false;
  if (m.step === "copy") say("Copying to the Switch… " + m.done + " of " + m.total + " files");
  else if (m.total > 0) say("Downloading HomeNX… " + Math.round(m.done / 1048576) + " of " + Math.round(m.total / 1048576) + " MB");
  else say("Getting the newest HomeNX…");
}

// One thing at a time on the Switch. `run` resolves to the line to show.
async function job(run) {
  working = true;
  $("fill").style.width = "0%";
  render();
  try {
    say(await run(), "good");
  } catch (e) {
    say(e.message, "bad");
  }
  working = false;
  $("bar").hidden = true;
  render();
}

function restart() {
  return injected.drive ? "Eject the drive, close USB mode on the Switch and start it." : "Restart the Switch to see it.";
}

function kept() {
  if (!injected.kept) return "";
  return " " + injected.kept + (injected.kept === 1 ? " file was" : " files were") +
    " in use and kept the old copy; inject from Hekate's USB mode for a clean one.";
}

$("go").addEventListener("click", () => job(async () => {
  injected = await call("inject");
  $("first").showModal();
  return "Everything is on your Switch" + (injected.build ? " (" + injected.build + ")" : "") + "." + kept();
}));

// The popup after an inject: which home menu the HOME button opens.
for (const button of document.querySelectorAll(".look")) {
  button.addEventListener("click", () => {
    const look = button.dataset.look;
    $("first").close();
    job(async () => {
      try {
        await call("look", { look });
      } catch (e) {
        throw new Error("Couldn't save that (" + e.message + ") Pick the style in NXToggle instead.");
      }
      return names[look] + " opens first. " + restart() + kept();
    });
  });
}

// The question needs an answer: Esc doesn't dismiss it.
$("first").addEventListener("cancel", e => e.preventDefault());

$("off").addEventListener("click", () => job(async () => {
  await call("off");
  return "The frontend is off: Nintendo's HOME menu is back after a restart. NXToggle, or injecting again, turns it on.";
}));

async function poll() {
  if (!working) {
    let seen = null;
    try { seen = await call("detect"); } catch (e) { }
    if (!working) {
      found = seen;
      render();
    }
  }
  setTimeout(poll, 2000);
}

render();
poll();
