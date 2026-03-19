{{/*
Expand the name of the chart.
*/}}
{{- define "mc-operator.name" -}}
{{- default .Chart.Name .Values.nameOverride | trunc 63 | trimSuffix "-" }}
{{- end }}

{{/*
Create a default fully qualified app name.
*/}}
{{- define "mc-operator.fullname" -}}
{{- if .Values.fullnameOverride }}
{{- .Values.fullnameOverride | trunc 63 | trimSuffix "-" }}
{{- else }}
{{- $name := default .Chart.Name .Values.nameOverride }}
{{- if contains $name .Release.Name }}
{{- .Release.Name | trunc 63 | trimSuffix "-" }}
{{- else }}
{{- printf "%s-%s" .Release.Name $name | trunc 63 | trimSuffix "-" }}
{{- end }}
{{- end }}
{{- end }}

{{/*
Create chart label.
*/}}
{{- define "mc-operator.chart" -}}
{{- printf "%s-%s" .Chart.Name .Chart.Version | replace "+" "_" | trunc 63 | trimSuffix "-" }}
{{- end }}

{{/*
Common labels.
*/}}
{{- define "mc-operator.labels" -}}
helm.sh/chart: {{ include "mc-operator.chart" . }}
{{ include "mc-operator.selectorLabels" . }}
{{- if .Chart.AppVersion }}
app.kubernetes.io/version: {{ .Chart.AppVersion | quote }}
{{- end }}
app.kubernetes.io/managed-by: {{ .Release.Service }}
{{- end }}

{{/*
Selector labels.
*/}}
{{- define "mc-operator.selectorLabels" -}}
app.kubernetes.io/name: {{ include "mc-operator.name" . }}
app.kubernetes.io/instance: {{ .Release.Name }}
{{- end }}

{{/*
Create the name of the service account to use.
*/}}
{{- define "mc-operator.serviceAccountName" -}}
{{- if .Values.serviceAccount.create }}
{{- default (include "mc-operator.fullname" .) .Values.serviceAccount.name }}
{{- else }}
{{- default "default" .Values.serviceAccount.name }}
{{- end }}
{{- end }}

{{/*
Image tag - defaults to chart appVersion.
*/}}
{{- define "mc-operator.imageTag" -}}
{{- .Values.image.tag | default .Chart.AppVersion }}
{{- end }}

{{/*
Webhook certificate secret name.
*/}}
{{- define "mc-operator.webhookCertSecret" -}}
{{- printf "%s-webhook-cert" (include "mc-operator.fullname" .) }}
{{- end }}

{{/*
Generate webhook TLS certificates.
Uses lookup to reuse existing certificates on upgrades.
Returns a dict with ca, cert, and key (all base64-encoded).
*/}}
{{- define "mc-operator.webhookCerts" -}}
{{- $secretName := include "mc-operator.webhookCertSecret" . -}}
{{- $secret := lookup "v1" "Secret" .Values.namespace.name $secretName -}}
{{- if and $secret $secret.data -}}
ca: {{ index $secret.data "ca.crt" }}
cert: {{ index $secret.data "tls.crt" }}
key: {{ index $secret.data "tls.key" }}
{{- else -}}
{{- $svcName := include "mc-operator.fullname" . -}}
{{- $ns := .Values.namespace.name -}}
{{- $cn := printf "%s.%s.svc" $svcName $ns -}}
{{- $altNames := list $cn (printf "%s.%s.svc.cluster.local" $svcName $ns) -}}
{{- $ca := genCA "mc-operator-ca" 3650 -}}
{{- $cert := genSignedCert $cn nil $altNames 3650 $ca -}}
ca: {{ $ca.Cert | b64enc }}
cert: {{ $cert.Cert | b64enc }}
key: {{ $cert.Key | b64enc }}
{{- end -}}
{{- end }}
