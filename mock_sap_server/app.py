"""
Mock SAP S/4HANA OData Service (API_OP_INVOICE_PROCESS_SRV)
Simulates enterprise ERP endpoints for invoice verification, vendor master data validation,
3-way matching (PO, Goods Receipt, Invoice), and automated GL clearing.
"""

from fastapi import FastAPI, HTTPException, status
from pydantic import BaseModel, Field
from typing import List, Optional
from datetime import datetime, timezone
import uuid

app = FastAPI(
    title="SAP S/4HANA Enterprise Invoice Mock API",
    version="1.0.0",
    description="Enterprise OData mock service supporting automated invoice validation and posting for UiPath REFramework bot."
)

# In-memory mock database of approved enterprise vendors
VENDORS_DB = {
    "10482910-0001": {
        "vendor_id": "VEND-10029",
        "name": "DELTA OILFIELD SERVICES LIMITED",
        "tin": "10482910-0001",
        "status": "ACTIVE",
        "currency": "USD",
        "payment_terms": "NET30",
        "withholding_tax_code": "WHT-5"
    },
    "2289410941": {
        "vendor_id": "VEND-20411",
        "name": "ATLANTIC LOGISTICS & MARINE LTD",
        "tin": "2289410941",
        "status": "ACTIVE",
        "currency": "NGN",
        "payment_terms": "NET45",
        "withholding_tax_code": "WHT-5"
    }
}

# In-memory mock Purchase Orders
PURCHASE_ORDERS_DB = {
    "4500982314": {
        "po_number": "4500982314",
        "vendor_id": "VEND-10029",
        "company_code": "NNPC-1000",
        "cost_center": "CC-OPS-OML119",
        "currency": "USD",
        "open_amount": 120000.00,
        "is_goods_received": True,
        "status": "RELEASED"
    },
    "4500114920": {
        "po_number": "4500114920",
        "vendor_id": "VEND-20411",
        "company_code": "NNPC-2000",
        "cost_center": "CC-LOG-NEPL",
        "currency": "NGN",
        "open_amount": 45000000.00,
        "is_goods_received": True,
        "status": "RELEASED"
    }
}

POSTED_DOCUMENTS = []

class InvoicePostRequest(BaseModel):
    invoice_number: str = Field(..., json_schema_extra={"example": "INV-2024-8841"})
    purchase_order_number: str = Field(..., json_schema_extra={"example": "4500982314"})
    tin: str = Field(..., json_schema_extra={"example": "10482910-0001"})
    currency: str = Field(..., json_schema_extra={"example": "USD"})
    net_amount: float = Field(..., gt=0)
    vat_amount: float = Field(..., ge=0)
    gross_amount: float = Field(..., gt=0)
    cost_center: Optional[str] = "CC-OPS-OML119"
    invoice_date: str = Field(..., json_schema_extra={"example": "2024-10-15"})

class InvoicePostResponse(BaseModel):
    sap_document_number: str
    fiscal_year: int
    posting_status: str
    message: str
    reconciliation_status: str
    timestamp: str

@app.get("/sap/opu/odata/sap/API_OP_INVOICE_PROCESS_SRV/Health")
def health_check():
    return {
        "status": "ONLINE",
        "system": "SAP S/4HANA 2023 Enterprise Core",
        "client": "100",
        "timestamp": datetime.now(timezone.utc).isoformat()
    }

@app.get("/sap/opu/odata/sap/API_OP_INVOICE_PROCESS_SRV/Vendors/{tin}")
def get_vendor_by_tin(tin: str):
    vendor = VENDORS_DB.get(tin)
    if not vendor:
        raise HTTPException(
            status_code=status.HTTP_404_NOT_FOUND,
            detail=f"Vendor with TIN '{tin}' not found in SAP Master Data."
        )
    return vendor

@app.get("/sap/opu/odata/sap/API_OP_INVOICE_PROCESS_SRV/PurchaseOrders/{po_number}")
def get_purchase_order(po_number: str):
    po = PURCHASE_ORDERS_DB.get(po_number)
    if not po:
        raise HTTPException(
            status_code=status.HTTP_404_NOT_FOUND,
            detail=f"Purchase Order '{po_number}' does not exist in SAP MM module."
        )
    return po

@app.post("/sap/opu/odata/sap/API_OP_INVOICE_PROCESS_SRV/PostInvoice", response_model=InvoicePostResponse)
def post_invoice_document(req: InvoicePostRequest):
    # 1. Vendor verification
    vendor = VENDORS_DB.get(req.tin)
    if not vendor:
        raise HTTPException(
            status_code=status.HTTP_422_UNPROCESSABLE_ENTITY,
            detail=f"Business Exception: TIN {req.tin} is not registered."
        )

    # 2. PO verification
    po = PURCHASE_ORDERS_DB.get(req.purchase_order_number)
    if not po:
        raise HTTPException(
            status_code=status.HTTP_422_UNPROCESSABLE_ENTITY,
            detail=f"Business Exception: PO {req.purchase_order_number} does not exist."
        )

    # 3. 3-Way Match Check (PO open amount vs Invoice Net Amount)
    amount_tolerance = 1.00 # $1 or 1 Naira tolerance
    if abs(po["open_amount"] - req.net_amount) > amount_tolerance:
        raise HTTPException(
            status_code=status.HTTP_422_UNPROCESSABLE_ENTITY,
            detail=f"3-Way Match Variance: PO open amount is {po['open_amount']} {po['currency']}, but invoice net is {req.net_amount} {req.currency}."
        )

    # 4. Generate SAP FI Accounting Document
    sap_doc_id = f"5100{len(POSTED_DOCUMENTS) + 1042:06d}"
    record = {
        "sap_document_number": sap_doc_id,
        "fiscal_year": datetime.now(timezone.utc).year,
        "posting_status": "POSTED_PARKED_FOR_PAYMENT",
        "message": f"Document {sap_doc_id} posted successfully to Company Code {po['company_code']}.",
        "reconciliation_status": "CLEARED_3WAY_MATCH",
        "timestamp": datetime.now(timezone.utc).isoformat(),
        "details": req.model_dump()
    }
    POSTED_DOCUMENTS.append(record)

    return InvoicePostResponse(
        sap_document_number=record["sap_document_number"],
        fiscal_year=record["fiscal_year"],
        posting_status=record["posting_status"],
        message=record["message"],
        reconciliation_status=record["reconciliation_status"],
        timestamp=record["timestamp"]
    )
