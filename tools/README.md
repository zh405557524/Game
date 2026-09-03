# 开发工具

放置构建、资源导入、格式转换、数据校验和发布辅助脚本。每个工具应说明运行环境、输入、输出、使用示例和是否会修改文件。

## 地图美术

- `node tools/map_art/validate_pipeline.mjs`：只读检查县级地图十二层、64主题和五阶段门禁。
- `node --test tests/map_art/validate_pipeline.test.mjs`：验证正常流程、越级、上游换版和“技术通过不等于人工批准”。
- `inspect_png.mjs` 与 `process_texture.mjs`：PNG检查和显式版本化处理；处理工具拒绝覆盖既有输出。
