import requests
import json

# Don't forget to register your mod at each orleans reboot, something like:
# curl -v -H 'Accept: application/json' -H 'Content-type: application/json' -d '{"modName":"WHMod", "webhookBaseUrl": "http://MY_IP:8989"}' http://localhost:10112/meta/registermod
# assuming you port-forwarded orleans to 10112

orleans = 'http://localhost:10112'  # change me if needed


from http.server import BaseHTTPRequestHandler, HTTPServer
class MyServer(BaseHTTPRequestHandler):
    def do_POST(self):
        print(self.path)
        reply = b'{}'
        if self.path == '/getmodinfo': # Reply with our popup menu actions
            reply=b'{"name": "WHMod", "actions":[{"id": 1, "label":"test webhook", "context":0}]}'
        elif self.path.split('/')[1] == 'action':
            # Player clicked on our popup menu
            pid = int(self.path.split('/')[2])
            # Invoke metamessage to route our request to the player client
            requests.post(orleans + '/meta/metamessage', json={
                    'targetType': 'player',
                    'targetId': pid, # playerid who clicked
                    'requestName': 'ModTriggerHudEventRequest',
                    'serializedPayload': json.dumps({
                            'eventName': "modinjectjs", # this will eval in the client
                            'eventPayload': 'CPPHud.addFailureNotification("Mod success: no error");'
                    })
            })
        self.send_response(200)
        self.send_header("Content-type", "application/json")
        self.send_header("Content-length", len(reply))
        self.end_headers()
        self.wfile.write(reply)
webServer = HTTPServer(('0.0.0.0', 8989), MyServer)
webServer.serve_forever()