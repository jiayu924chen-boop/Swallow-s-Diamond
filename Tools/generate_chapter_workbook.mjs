import fs from "node:fs/promises";
import path from "node:path";
import { fileURLToPath } from "node:url";
import { SpreadsheetFile, Workbook } from "file:///C:/Users/jiayu/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/@oai/artifact-tool/dist/artifact_tool.mjs";

const repoRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "..");
const levelDir = path.join(repoRoot, "Assets", "levels");
const outputDir = path.join(repoRoot, "outputs");
const outputPath = path.join(outputDir, "chapter_level_config.xlsx");
const previewPath = path.join(outputDir, "chapter_level_config_preview.png");

const chapters = [
  { chapter: 1, label: "章节一", levels: [1, 2] },
  { chapter: 2, label: "章节二", levels: [3, 4] },
  { chapter: 3, label: "章节三", levels: [5, 6] },
  { chapter: 4, label: "章节四", levels: [7, 10] },
];

function fileNameForLevel(level) {
  return `level-${String(level).padStart(3, "0")}.json`;
}

async function readLevelMeta(level) {
  const fileName = fileNameForLevel(level);
  const filePath = path.join(levelDir, fileName);
  try {
    const json = JSON.parse(await fs.readFile(filePath, "utf8"));
    return {
      fileName,
      exists: true,
      rows: Number(json.rows) || "",
      cols: Number(json.cols) || "",
      carpetCount: Array.isArray(json.carpets) ? json.carpets.length : "",
    };
  } catch {
    return { fileName, exists: false, rows: "", cols: "", carpetCount: "" };
  }
}

function styleHeader(range) {
  range.format = {
    fill: "#1f4e5f",
    font: { bold: true, color: "#ffffff" },
    wrapText: true,
  };
}

function styleTitle(range) {
  range.format = {
    fill: "#e9dfc7",
    font: { bold: true, color: "#2a2a2a", size: 16 },
  };
}

function styleTable(range) {
  range.format.borders = {
    insideHorizontal: { style: "thin", color: "#d7cbb4" },
    insideVertical: { style: "thin", color: "#d7cbb4" },
    top: { style: "medium", color: "#8f8066" },
    bottom: { style: "medium", color: "#8f8066" },
    left: { style: "thin", color: "#d7cbb4" },
    right: { style: "thin", color: "#d7cbb4" },
  };
}

async function main() {
  await fs.mkdir(outputDir, { recursive: true });

  const metaByLevel = new Map();
  for (const chapter of chapters) {
    for (const level of chapter.levels) {
      metaByLevel.set(level, await readLevelMeta(level));
    }
  }

  const workbook = Workbook.create();
  const chapterSheet = workbook.worksheets.add("章节配置");
  const mapSheet = workbook.worksheets.add("逐关映射");
  const noteSheet = workbook.worksheets.add("使用说明");

  for (const sheet of [chapterSheet, mapSheet, noteSheet]) {
    sheet.showGridLines = false;
  }

  chapterSheet.getRange("A1:F1").merge();
  chapterSheet.getRange("A1").values = [["章节关卡配置"]];
  styleTitle(chapterSheet.getRange("A1:F1"));
  chapterSheet.getRange("A3:F3").values = [[
    "章节序号",
    "章节名称",
    "关卡列表",
    "读取 JSON 文件",
    "当前默认进度起点",
    "备注",
  ]];
  styleHeader(chapterSheet.getRange("A3:F3"));

  const chapterRows = chapters.map((chapter, index) => {
    const files = chapter.levels.map((level) => metaByLevel.get(level).fileName);
    return [
      chapter.chapter,
      chapter.label,
      chapter.levels.join(", "),
      files.join(", "),
      chapter.levels[0],
      `对应 menu_config.json buttons[${index}].levels`,
    ];
  });
  chapterSheet.getRangeByIndexes(3, 0, chapterRows.length, 6).values = chapterRows;
  styleTable(chapterSheet.getRange("A3:F7"));
  chapterSheet.getRange("A3:F7").format.wrapText = true;
  chapterSheet.getRange("A1:F7").format.autofitColumns();
  chapterSheet.getRange("A1:F7").format.autofitRows();
  chapterSheet.freezePanes.freezeRows(3);

  mapSheet.getRange("A1:H1").merge();
  mapSheet.getRange("A1").values = [["逐关 JSON 映射"]];
  styleTitle(mapSheet.getRange("A1:H1"));
  mapSheet.getRange("A3:H3").values = [[
    "章节序号",
    "章节名称",
    "章节内顺序",
    "关卡号",
    "JSON 文件",
    "文件存在",
    "棋盘行列",
    "地毯数",
  ]];
  styleHeader(mapSheet.getRange("A3:H3"));

  const mapRows = [];
  for (const chapter of chapters) {
    chapter.levels.forEach((level, index) => {
      const meta = metaByLevel.get(level);
      mapRows.push([
        chapter.chapter,
        chapter.label,
        index + 1,
        level,
        meta.fileName,
        meta.exists ? "是" : "否",
        meta.exists ? `${meta.rows} x ${meta.cols}` : "",
        meta.carpetCount,
      ]);
    });
  }
  mapSheet.getRangeByIndexes(3, 0, mapRows.length, 8).values = mapRows;
  styleTable(mapSheet.getRange("A3:H11"));
  mapSheet.getRange("A3:H11").format.wrapText = true;
  mapSheet.getRange("A1:H11").format.autofitColumns();
  mapSheet.getRange("A1:H11").format.autofitRows();
  mapSheet.freezePanes.freezeRows(3);

  noteSheet.getRange("A1:D1").merge();
  noteSheet.getRange("A1").values = [["使用说明"]];
  styleTitle(noteSheet.getRange("A1:D1"));
  noteSheet.getRange("A3:B7").values = [
    ["配置入口", "Assets/StreamingAssets/Menu/menu_config.json"],
    ["读取规则", "buttons[n].levels 决定章节按钮按进度读取哪些关卡"],
    ["关卡文件目录", "Assets/levels"],
    ["文件命名", "当前项目使用 level-001.json 这类尾部数字命名，游戏会解析文件名里的关卡数字"],
    ["第四章默认", "章节四读取 7, 10 两个当前存在的关卡"],
  ];
  styleTable(noteSheet.getRange("A3:B7"));
  noteSheet.getRange("A3:A7").format = {
    fill: "#f4ead5",
    font: { bold: true, color: "#2a2a2a" },
  };
  noteSheet.getRange("A1:D7").format.wrapText = true;
  noteSheet.getRange("A1:D7").format.autofitColumns();
  noteSheet.getRange("A1:D7").format.autofitRows();

  const preview = await workbook.render({
    sheetName: "章节配置",
    autoCrop: "all",
    scale: 1,
    format: "png",
  });
  await fs.writeFile(previewPath, new Uint8Array(await preview.arrayBuffer()));

  const output = await SpreadsheetFile.exportXlsx(workbook);
  await output.save(outputPath);

  const inspect = await workbook.inspect({
    kind: "workbook,sheet,region",
    sheetId: "章节配置",
    range: "A1:F7",
    maxChars: 4000,
  });
  console.log(inspect);
  console.log(outputPath);
  console.log(previewPath);
}

main().catch((error) => {
  console.error(error);
  process.exit(1);
});
