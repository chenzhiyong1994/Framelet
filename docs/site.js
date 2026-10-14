"use strict";

// Language links, downloads, FAQs and screenshot links also work without JavaScript.
const dialog = document.querySelector("#image-dialog");
const dialogImage = document.querySelector("#dialog-image");
const dialogTitle = document.querySelector("#dialog-title");

if (dialog && dialogImage && dialogTitle && typeof dialog.showModal === "function") {
  for (const link of document.querySelectorAll("[data-screenshot]")) {
    link.addEventListener("click", event => {
      if (event.ctrlKey || event.metaKey || event.shiftKey || event.altKey) return;
      event.preventDefault();
      const sourceImage = link.querySelector("img");
      dialogImage.src = link.href;
      dialogImage.alt = sourceImage.alt;
      dialogTitle.textContent = link.dataset.screenshot;
      dialog.showModal();
    });
  }

  document.querySelector(".dialog-close").addEventListener("click", () => dialog.close());
  dialog.addEventListener("click", event => {
    if (event.target !== dialog) return;
    const bounds = dialog.getBoundingClientRect();
    if (event.clientX < bounds.left || event.clientX > bounds.right || event.clientY < bounds.top || event.clientY > bounds.bottom) dialog.close();
  });
}
