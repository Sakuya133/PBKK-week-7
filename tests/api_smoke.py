"""Smoke test API pada database pengembangan yang telah di-seed dengan --demo."""
import json
import os
import urllib.error
import urllib.request
import uuid

base = os.environ.get('POS_API_TEST_URL', 'http://127.0.0.1:5077').rstrip('/')
if not (base.startswith('http://127.0.0.1:') or base.startswith('http://localhost:')):
    raise SystemExit('Tes ini hanya untuk API development pada loopback.')


def call(method, path, token=None, body=None):
    data = None if body is None else json.dumps(body).encode()
    headers = {'Content-Type': 'application/json'} if data is not None else {}
    if token:
        headers['Authorization'] = 'Bearer ' + token
    request = urllib.request.Request(base + path, data=data, headers=headers, method=method)
    try:
        with urllib.request.urlopen(request, timeout=15) as response:
            return response.status, json.load(response)
    except urllib.error.HTTPError as error:
        return error.code, json.load(error)


def login(user, password):
    status, result = call('POST', '/api/auth/login', body={'username': user, 'password': password})
    assert status == 200 and result['success'], (status, result)
    return result['value']['token']


admin = login('admin', 'Admin123!')
kasir = login('kasir', 'Kasir123!')
pelanggan = login('pelanggan', 'Pelanggan123!')
pelanggan2 = login('pelanggan2', 'Pelanggan234!')
status, kiosk_catalog = call('GET', '/api/kiosk/catalog')
assert status == 200 and kiosk_catalog['value'], (status, kiosk_catalog)
status, catalog = call('GET', '/api/products/catalog', pelanggan)
assert status == 200 and catalog['value']
status, denied = call('GET', '/api/dashboard/today', pelanggan)
assert status == 403 and denied['error'] == 'Forbidden'
status, customers = call('GET', '/api/transactions/customers', kasir)
assert status == 200 and customers['value']
customer_id = next(row['id'] for row in customers['value'] if row['fullName'] == 'Pelanggan Demo')
request_id = str(uuid.uuid4())
checkout = {'customerId': customer_id, 'paymentMethod': 'Tunai',
            'items': [{'productId': catalog['value'][0]['id'], 'quantity': 2}],
            'requestId': request_id}
status, sold = call('POST', '/api/transactions/checkout', kasir, checkout)
assert status == 200 and sold['value']['total'] == 2 * catalog['value'][0]['price'], (status, sold)
transaction_id = sold['value']['id']
status, duplicate = call('POST', '/api/transactions/checkout', kasir, checkout)
assert status == 409 and duplicate['error'] == 'Conflict'
status, own = call('GET', f'/api/transactions/{transaction_id}', pelanggan)
assert status == 200 and own['value']['id'] == transaction_id
status, other = call('GET', f'/api/transactions/{transaction_id}', pelanggan2)
assert status == 404 and other['error'] == 'NotFound'
guest_request = {'guestName': 'Tamu Smoke', 'paymentMethod': 'Qris',
                 'items': [{'productId': kiosk_catalog['value'][0]['id'], 'quantity': 2}],
                 'requestId': str(uuid.uuid4())}
status, guest_order = call('POST', '/api/kiosk/orders', body=guest_request)
assert status == 200 and guest_order['value']['guestName'] == 'Tamu Smoke', (status, guest_order)
status, guest_retry = call('POST', '/api/kiosk/orders', body=guest_request)
assert status == 200 and guest_retry['value']['id'] == guest_order['value']['id'], (status, guest_retry)
status, guest_queue = call('GET', '/api/kiosk/orders/pending')
assert status == 401
status, guest_queue = call('GET', '/api/kiosk/orders/pending', kasir)
assert status == 200 and any(x['id'] == guest_order['value']['id'] for x in guest_queue['value'])
status, guest_sale = call('POST', f"/api/kiosk/orders/{guest_order['value']['id']}/complete", kasir)
assert status == 200 and guest_sale['value']['customerName'] == 'Tamu Smoke', (status, guest_sale)
status, guest_detail = call('GET', f"/api/transactions/{guest_sale['value']['id']}", kasir)
assert status == 200 and guest_detail['value']['items'][0]['quantity'] == 2
status, repeated_completion = call('POST', f"/api/kiosk/orders/{guest_order['value']['id']}/complete", kasir)
assert status == 409 and repeated_completion['error'] == 'Conflict'
status, dashboard = call('GET', '/api/dashboard/today', admin)
assert status == 200 and dashboard['value']['transactionCount'] >= 2
status, logout = call('POST', '/api/auth/logout', pelanggan)
assert status == 200 and logout['success']
status, after_logout = call('GET', '/api/products/catalog', pelanggan)
assert status == 401 and after_logout['error'] == 'Unauthorized'
statuses = [call('POST', '/api/auth/login', body={'username': 'tidak_ada', 'password': 'salah'})[0]
            for _ in range(12)]
assert 429 in statuses, statuses
print('API smoke: login, RBAC, checkout, kiosk tamu, idempotensi, konfirmasi kasir, isolasi pelanggan, dashboard, logout, rate limit LULUS')
