const { contextBridge, ipcRenderer } = require("electron");
contextBridge.exposeInMainWorld("steam", {
  request: (command, payload = {}) =>
    ipcRenderer.invoke("app:request", command, payload),
  onUpdate: (callback) => {
    const listener = (_, data) => callback(data);
    ipcRenderer.on("app:update", listener);
    return () => ipcRenderer.removeListener("app:update", listener);
  },
  onSteamStatus: (callback) => {
    const listener = (_, data) => callback(data);
    ipcRenderer.on("app:steam-status", listener);
    return () => ipcRenderer.removeListener("app:steam-status", listener);
  },
  onQuery: (callback) => {
    const listener = (_, data) => callback(data);
    ipcRenderer.on("query:state", listener);
    return () => ipcRenderer.removeListener("query:state", listener);
  },
});
