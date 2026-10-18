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
status, dashboard = call('GET', '/api/dashboard/today', admin)
assert status == 200 and dashboard['value']['transactionCount'] >= 1
status, logout = call('POST', '/api/auth/logout', pelanggan)
assert status == 200 and logout['success']
status, after_logout = call('GET', '/api/products/catalog', pelanggan)
assert status == 401 and after_logout['error'] == 'Unauthorized'
statuses = [call('POST', '/api/auth/login', body={'username': 'tidak_ada', 'password': 'salah'})[0]
            for _ in range(12)]
assert 429 in statuses, statuses
print('API smoke: login, RBAC, checkout, idempotensi, isolasi pelanggan, dashboard, logout, rate limit LULUS')
