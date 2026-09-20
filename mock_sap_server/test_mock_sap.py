from fastapi.testclient import TestClient
from app import app

client = TestClient(app)

def test_health_check():
    response = client.get("/sap/opu/odata/sap/API_OP_INVOICE_PROCESS_SRV/Health")
    assert response.status_code == 200
    assert response.json()["status"] == "ONLINE"

def test_get_vendor_success():
    response = client.get("/sap/opu/odata/sap/API_OP_INVOICE_PROCESS_SRV/Vendors/10482910-0001")
    assert response.status_code == 200
    assert response.json()["name"] == "DELTA OILFIELD SERVICES LIMITED"

def test_get_vendor_not_found():
    response = client.get("/sap/opu/odata/sap/API_OP_INVOICE_PROCESS_SRV/Vendors/UNKNOWN-999")
    assert response.status_code == 404

def test_post_invoice_success_3way_match():
    payload = {
        "invoice_number": "INV-2024-8841",
        "purchase_order_number": "4500982314",
        "tin": "10482910-0001",
        "currency": "USD",
        "net_amount": 120000.00,
        "vat_amount": 9000.00,
        "gross_amount": 129000.00,
        "cost_center": "CC-OPS-OML119",
        "invoice_date": "2024-10-15"
    }
    response = client.post("/sap/opu/odata/sap/API_OP_INVOICE_PROCESS_SRV/PostInvoice", json=payload)
    assert response.status_code == 200
    data = response.json()
    assert data["posting_status"] == "POSTED_PARKED_FOR_PAYMENT"
    assert data["reconciliation_status"] == "CLEARED_3WAY_MATCH"
    assert "5100" in data["sap_document_number"]

def test_post_invoice_variance_fails_3way_match():
    payload = {
        "invoice_number": "INV-MISMATCH",
        "purchase_order_number": "4500982314",
        "tin": "10482910-0001",
        "currency": "USD",
        "net_amount": 999999.00,  # Does not match PO amount of 120,000.00
        "vat_amount": 0.00,
        "gross_amount": 999999.00,
        "cost_center": "CC-OPS-OML119",
        "invoice_date": "2024-10-15"
    }
    response = client.post("/sap/opu/odata/sap/API_OP_INVOICE_PROCESS_SRV/PostInvoice", json=payload)
    assert response.status_code == 422
    assert "3-Way Match Variance" in response.json()["detail"]
