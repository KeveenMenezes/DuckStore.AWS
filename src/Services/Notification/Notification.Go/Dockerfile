# ---- Build stage ----
FROM golang:1.23-alpine AS builder

WORKDIR /app

COPY go.mod ./
COPY go.sum* ./
RUN go mod download 2>/dev/null || true

COPY . .
RUN go mod tidy && CGO_ENABLED=0 GOOS=linux go build -o /notification .

# ---- Runtime stage ----
FROM alpine:3.20

RUN apk add --no-cache ca-certificates

COPY --from=builder /notification /notification
COPY events.json /events.json

ENV APP_MODE=""
ENV AWS_ENDPOINT_URL=""
ENV AWS_REGION="us-east-1"
ENV SQS_QUEUE_URL=""
ENV SQS_QUEUE_NAME="duckstore-notifications"
ENV DYNAMODB_TABLE="duckstore-notifications"

ENTRYPOINT ["/notification"]
