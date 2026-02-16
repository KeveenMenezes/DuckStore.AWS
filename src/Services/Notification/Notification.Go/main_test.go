package main

import (
	"encoding/json"
	"testing"
)

func TestNotificationEventUnmarshal(t *testing.T) {
	body := `{
		"id": "notif-001",
		"eventType": "BasketCheckoutEvent",
		"userName": "johndoe",
		"customerId": "550e8400-e29b-41d4-a716-446655440000",
		"totalPrice": 259.99,
		"firstName": "John",
		"lastName": "Doe",
		"emailAddress": "john.doe@example.com",
		"addressLine": "123 Duck Street",
		"country": "Brazil",
		"state": "SP",
		"zipCode": "01001-000",
		"cardName": "John Doe",
		"cardNumber": "**** **** **** 1234",
		"expiration": "12/28",
		"cvv": "***",
		"paymentMethod": 1,
		"occurredOn": "2026-02-16T12:00:00Z"
	}`

	var event NotificationEvent
	err := json.Unmarshal([]byte(body), &event)
	if err != nil {
		t.Fatalf("failed to unmarshal: %v", err)
	}

	if event.ID != "notif-001" {
		t.Errorf("expected ID 'notif-001', got %q", event.ID)
	}
	if event.EventType != "BasketCheckoutEvent" {
		t.Errorf("expected EventType 'BasketCheckoutEvent', got %q", event.EventType)
	}
	if event.UserName != "johndoe" {
		t.Errorf("expected UserName 'johndoe', got %q", event.UserName)
	}
	if event.TotalPrice != 259.99 {
		t.Errorf("expected TotalPrice 259.99, got %f", event.TotalPrice)
	}
	if event.PaymentMethod != 1 {
		t.Errorf("expected PaymentMethod 1, got %d", event.PaymentMethod)
	}
	if event.EmailAddress != "john.doe@example.com" {
		t.Errorf("expected EmailAddress 'john.doe@example.com', got %q", event.EmailAddress)
	}
}

func TestNotificationEventDefaultValues(t *testing.T) {
	body := `{"eventType": "BasketCheckoutEvent", "userName": "test"}`

	var event NotificationEvent
	err := json.Unmarshal([]byte(body), &event)
	if err != nil {
		t.Fatalf("failed to unmarshal: %v", err)
	}

	// ID should be empty (processMessage fills it in)
	if event.ID != "" {
		t.Errorf("expected empty ID, got %q", event.ID)
	}
	if event.EventType != "BasketCheckoutEvent" {
		t.Errorf("expected EventType 'BasketCheckoutEvent', got %q", event.EventType)
	}
}

func TestGetEnvOrDefault(t *testing.T) {
	result := getEnvOrDefault("NONEXISTENT_ENV_VAR_FOR_TEST", "fallback")
	if result != "fallback" {
		t.Errorf("expected 'fallback', got %q", result)
	}

	t.Setenv("TEST_ENV_VAR_SET", "myvalue")
	result = getEnvOrDefault("TEST_ENV_VAR_SET", "fallback")
	if result != "myvalue" {
		t.Errorf("expected 'myvalue', got %q", result)
	}
}

func TestNewNotificationID(t *testing.T) {
	id := NewNotificationID()
	if len(id) == 0 {
		t.Error("expected non-empty NotificationID")
	}
	if id[:6] != "notif-" {
		t.Errorf("expected ID to start with 'notif-', got %q", id)
	}
}
