FROM public.ecr.aws/aws-cli/aws-cli@sha256:4e569d37c0b93893f660946f9b00a442b1172b1c4ba86338a3a1af3937ed62b5
RUN dnf install -y --allowerasing socat gnupg2 && dnf clean all
COPY aws-plugin-signing-key.asc /tmp/aws-plugin-signing-key.asc
RUN gpg --batch --import /tmp/aws-plugin-signing-key.asc \
    && test "$(gpg --batch --with-colons --fingerprint 2C4D4AFF6F6757EE | awk -F: '/^fpr:/ {print $10; exit}')" = "7959637124CE093AD501D47A2C4D4AFF6F6757EE" \
    && curl --fail --show-error --location --retry 3 https://s3.amazonaws.com/session-manager-downloads/plugin/latest/linux_64bit/session-manager-plugin.rpm -o /tmp/plugin.rpm \
    && curl --fail --show-error --location --retry 3 https://s3.amazonaws.com/session-manager-downloads/plugin/latest/linux_64bit/session-manager-plugin.rpm.sig -o /tmp/plugin.rpm.sig \
    && gpg --batch --verify /tmp/plugin.rpm.sig /tmp/plugin.rpm \
    && dnf install -y /tmp/plugin.rpm \
    && rm -f /tmp/plugin.rpm /tmp/plugin.rpm.sig \
    && dnf clean all \
    && session-manager-plugin --version
COPY start-tunnel.sh /usr/local/bin/start-tunnel.sh
RUN sed -i 's/\r$//' /usr/local/bin/start-tunnel.sh
EXPOSE 9443
ENTRYPOINT ["/bin/sh", "/usr/local/bin/start-tunnel.sh"]


