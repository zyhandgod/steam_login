const { contextBridge, ipcRenderer } = require("electron");
contextBridge.exposeInMainWorld("query", {
  action: (action) => ipcRenderer.invoke("query:action", action),
  subscribe: (callback) => {
    ipcRenderer.on("query:state", (_, state) => callback(state));
    ipcRenderer.send("query:ready");
  },
});
