const YarpGatewayApiUrl =
  process.env["services__yarp-api-gateway__https__0"] ||
  process.env["services__yarp-api-gateway__http__0"] ||
  "http://localhost:5050";

const isDevelopment = process.env["NODE_ENV"] === "development";

module.exports = {
  "/catalog-service": {
    target: YarpGatewayApiUrl,
    secure: !isDevelopment,
    changeOrigin: true,
  },
  "/ordering-service": {
    target: YarpGatewayApiUrl,
    secure: !isDevelopment,
    changeOrigin: true,
  },
  "/basket-service": {
    target: YarpGatewayApiUrl,
    secure: !isDevelopment,
    changeOrigin: true,
  },
};
