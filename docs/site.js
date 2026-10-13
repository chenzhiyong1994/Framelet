"use strict";

// Product screenshots stay usable without JavaScript; enhancements only reveal when ready.
const views = {
  editor: { src: "images/framelet-editor.jpg", alt: "Framelet 图像编辑器实际界面，菜单、工具栏、标签和画布分行排列", label: "图像编辑器", caption: "实际大小，清楚落笔。截图以 100% 显示，编辑、复制、保存就在手边。", width: 1266, height: 813 },
  recorder: { src: "images/framelet-recorder.png", alt: "Framelet 录屏设置实际界面，包含录制范围、帧率、鼠标选项和开始按钮", label: "录屏设置", caption: "范围、选项，一目了然。无声录屏无需配置引擎，选好区域就能开始。", width: 394, height: 384 },
  panel: { src: "images/framelet-panel.jpg", alt: "Framelet 浮动工具栏实际界面，包含截图、录屏、输出与设置入口", label: "浮动工具栏", caption: "常用入口，留在手边。紧凑工具栏支持贴边收起，需要时随时展开。", width: 413, height: 73 }
};
const image = document.querySelector("#product-image");
const imageButton = document.querySelector(".screenshot-button");
const stage = document.querySelector(".screenshot-stage");
const caption = document.querySelector("#view-caption");
const switches = [...document.querySelectorAll("[data-view]")];
const dialog = document.querySelector("#image-dialog");
const dialogImage = document.querySelector("#dialog-image");
let currentView = "editor";

for (const button of switches) {
  button.addEventListener("click", () => {
    currentView = button.dataset.view;
    const view = views[currentView];
    image.src = view.src;
    image.alt = view.alt;
    image.width = view.width;
    image.height = view.height;
    stage.dataset.mode = currentView;
    caption.textContent = view.caption;
    imageButton.setAttribute("aria-label", `放大查看${view.label}截图`);
    for (const item of switches) item.setAttribute("aria-pressed", String(item === button));
  });
}
document.querySelector(".view-switcher").hidden = false;

if (typeof dialog.showModal === "function") {
  imageButton.disabled = false;
  imageButton.addEventListener("click", () => {
    const view = views[currentView];
    dialogImage.src = view.src;
    dialogImage.alt = view.alt;
    document.querySelector("#dialog-title").textContent = `${view.label} · 实际界面`;
    dialog.showModal();
  });
  document.querySelector(".dialog-close").addEventListener("click", () => dialog.close());
  dialog.addEventListener("click", event => {
    if (event.target !== dialog) return;
    const bounds = dialog.getBoundingClientRect();
    if (event.clientX < bounds.left || event.clientX > bounds.right || event.clientY < bounds.top || event.clientY > bounds.bottom) dialog.close();
  });
}
