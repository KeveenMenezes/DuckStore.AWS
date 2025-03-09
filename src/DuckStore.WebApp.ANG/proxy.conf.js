const catalogApiUrl =
  process.env["services__catalogapi__https__0"] ||
  process.env["services__catalogapi__http__0"] ||
  "http://localhost:5050";

const isDevelopment = process.env["NODE_ENV"] === "development";

module.exports = {
  "/catalogapi": {
    target: catalogApiUrl,
    secure: !isDevelopment,
    changeOrigin: true,
      pathRewrite: {
      "^/catalogapi": "",
    },
  },
};
